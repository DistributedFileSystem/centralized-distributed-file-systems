using System.Globalization;
using System.Security.Cryptography;
using Dfs.Shared.Messages;
using Microsoft.Data.Sqlite;

namespace Dfs.DataServer.Storage;

/// <summary>
/// Manages chunk files on disk and records chunk metadata in local SQLite index (DATA_DIR/index.db)
/// according to docs/data-model.md.
/// </summary>
public sealed class ChunkStorage : IChunkStorage, IDisposable
{
    private readonly string _dataDir;
    private readonly long _configuredCapacity;
    private readonly SqliteConnection _db;
    private readonly object _dbLock = new();

    /// <inheritdoc />
    public string ChunksDir => Path.Combine(_dataDir, "chunks");

    /// <summary>Path to the local index SQLite database.</summary>
    public string IndexPath => Path.Combine(_dataDir, "index.db");

    /// <summary>
    /// Initializes a new instance of <see cref="ChunkStorage"/>.
    /// </summary>
    /// <param name="dataDir">Base directory for chunks and index.</param>
    /// <param name="configuredCapacity">Optional explicit capacity override.</param>
    public ChunkStorage(string dataDir, long configuredCapacity = 0)
    {
        _dataDir = Path.GetFullPath(dataDir);
        _configuredCapacity = configuredCapacity;

        Directory.CreateDirectory(_dataDir);
        Directory.CreateDirectory(ChunksDir);

        _db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = IndexPath,
            Pooling = false
        }.ToString());
        _db.Open();

        InitializeIndex();
    }

    private void InitializeIndex()
    {
        lock (_dbLock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS chunks (
                    chunk_id TEXT PRIMARY KEY,
                    size INTEGER NOT NULL,
                    sha256 TEXT NOT NULL,
                    created_at TEXT NOT NULL
                );
                """;
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Resolves the chunk file path based on its first 2 characters.</summary>
    public string GetChunkFilePath(string chunkId)
    {
        var prefix = chunkId.Length >= 2 ? chunkId[..2] : "__";
        return Path.Combine(ChunksDir, prefix, $"{chunkId}.chunk");
    }

    /// <inheritdoc />
    public async Task<WriteChunkResult> WriteChunkAsync(string chunkId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(chunkId))
            throw new DfsException(ErrorCode.BadRequest, "ChunkId must not be empty.");

        var finalPath = GetChunkFilePath(chunkId);
        var dir = Path.GetDirectoryName(finalPath)!;
        Directory.CreateDirectory(dir);

        var tempPath = Path.Combine(dir, $"{chunkId}.tmp.{Guid.NewGuid():N}");

        var sha256 = Convert.ToHexStringLower(SHA256.HashData(data.Span));

        await using (var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
        {
            await fs.WriteAsync(data, cancellationToken).ConfigureAwait(false);
            await fs.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(tempPath, finalPath, overwrite: true);

        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFFZ", CultureInfo.InvariantCulture);
        lock (_dbLock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                INSERT INTO chunks (chunk_id, size, sha256, created_at)
                VALUES ($id, $size, $sha, $at)
                ON CONFLICT(chunk_id) DO UPDATE SET
                    size = $size,
                    sha256 = $sha,
                    created_at = $at
                """;
            cmd.Parameters.AddWithValue("$id", chunkId);
            cmd.Parameters.AddWithValue("$size", data.Length);
            cmd.Parameters.AddWithValue("$sha", sha256);
            cmd.Parameters.AddWithValue("$at", now);
            cmd.ExecuteNonQuery();
        }

        return new WriteChunkResult(data.Length, sha256);
    }

    /// <inheritdoc />
    public async Task<ReadChunkResult> ReadChunkAsync(string chunkId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(chunkId))
            throw new DfsException(ErrorCode.BadRequest, "ChunkId must not be empty.");

        var finalPath = GetChunkFilePath(chunkId);
        if (!File.Exists(finalPath))
            throw new DfsException(ErrorCode.NotFound, $"Chunk '{chunkId}' not found.");

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(finalPath, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            throw new DfsException(ErrorCode.NotFound, $"Chunk '{chunkId}' not found.");
        }

        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return new ReadChunkResult(bytes, bytes.Length, sha256);
    }

    /// <inheritdoc />
    public Task<int> DeleteChunksAsync(IEnumerable<string> chunkIds, CancellationToken cancellationToken = default)
    {
        var deleted = 0;
        foreach (var chunkId in chunkIds)
        {
            if (string.IsNullOrWhiteSpace(chunkId)) continue;

            var finalPath = GetChunkFilePath(chunkId);
            if (File.Exists(finalPath))
            {
                try
                {
                    File.Delete(finalPath);
                    deleted++;
                }
                catch (IOException)
                {
                    // Ignore transient file IO issue during delete
                }
            }

            lock (_dbLock)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "DELETE FROM chunks WHERE chunk_id = $id";
                cmd.Parameters.AddWithValue("$id", chunkId);
                cmd.ExecuteNonQuery();
            }
        }

        return Task.FromResult(deleted);
    }

    /// <inheritdoc />
    public Task<int> GetChunkCountAsync(CancellationToken cancellationToken = default)
    {
        lock (_dbLock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM chunks";
            var count = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
            return Task.FromResult(count);
        }
    }

    /// <inheritdoc />
    public Task<long> GetFreeBytesAsync(CancellationToken cancellationToken = default)
    {
        long free;
        try
        {
            var root = Path.GetPathRoot(_dataDir);
            if (!string.IsNullOrEmpty(root))
            {
                var drive = new DriveInfo(root);
                free = drive.AvailableFreeSpace;
            }
            else
            {
                free = 80L * 1024 * 1024 * 1024;
            }
        }
        catch
        {
            free = 80L * 1024 * 1024 * 1024;
        }

        if (_configuredCapacity > 0 && free > _configuredCapacity)
        {
            free = _configuredCapacity;
        }

        return Task.FromResult(free);
    }

    /// <inheritdoc />
    public Task<long> GetCapacityBytesAsync(CancellationToken cancellationToken = default)
    {
        if (_configuredCapacity > 0)
        {
            return Task.FromResult(_configuredCapacity);
        }

        try
        {
            var root = Path.GetPathRoot(_dataDir);
            if (!string.IsNullOrEmpty(root))
            {
                var drive = new DriveInfo(root);
                return Task.FromResult(drive.TotalSize);
            }
        }
        catch
        {
            // Fallback if DriveInfo is not supported on this platform/path
        }

        return Task.FromResult(100L * 1024 * 1024 * 1024);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredChunkInfo>> ListChunksAsync(CancellationToken cancellationToken = default)
    {
        lock (_dbLock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT chunk_id, size, sha256 FROM chunks ORDER BY chunk_id";
            using var reader = cmd.ExecuteReader();
            var list = new List<StoredChunkInfo>();
            while (reader.Read())
            {
                list.Add(new StoredChunkInfo(reader.GetString(0), reader.GetInt64(1), reader.GetString(2)));
            }
            return Task.FromResult<IReadOnlyList<StoredChunkInfo>>(list);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_dbLock)
        {
            _db.Dispose();
        }
    }
}
