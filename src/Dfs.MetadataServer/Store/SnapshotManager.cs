using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Dfs.MetadataServer.Store;

/// <summary>
/// Manages atomic creation, validation, and restoration of SQLite metadata snapshots.
/// </summary>
internal static class SnapshotManager
{
    /// <summary>Filename of the SQLite snapshot database.</summary>
    public const string SnapshotDbFileName = "snapshot.db";

    /// <summary>Filename of the snapshot metadata JSON file.</summary>
    public const string SnapshotMetaFileName = "snapshot.meta.json";

    private const string TimeFormat = "yyyy-MM-ddTHH:mm:ss.FFFFFFFZ";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>Serialized metadata written to <c>snapshot.meta.json</c>.</summary>
    internal sealed record SnapshotMeta(long LastSeq, string CreatedAt, string Sha256);

    /// <summary>Validated snapshot files and metadata discovered on startup.</summary>
    internal sealed record ValidSnapshot(string DbPath, string MetaPath, long LastSeq, string CreatedAt, string Sha256);

    /// <summary>
    /// Atomically writes <c>snapshot.db</c> and <c>snapshot.meta.json</c> into <paramref name="snapshotDir"/>.
    /// Existing valid snapshots are left undisturbed if an error occurs before rename.
    /// </summary>
    public static void WriteSnapshot(SqliteMetadataState state, string snapshotDir, long lastSeq)
    {
        Directory.CreateDirectory(snapshotDir);

        var dbPath = Path.Combine(snapshotDir, SnapshotDbFileName);
        var metaPath = Path.Combine(snapshotDir, SnapshotMetaFileName);
        var dbTmp = dbPath + ".tmp";
        var metaTmp = metaPath + ".tmp";

        try
        {
            DeleteSqliteFiles(dbTmp);
            TryDeleteFile(metaTmp);

            state.BackupTo(dbTmp);

            using (var fs = new FileStream(dbTmp, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                fs.Flush(true);
            }

            var sha256 = ComputeSha256Hex(dbTmp);
            var meta = new SnapshotMeta(
                lastSeq,
                DateTime.UtcNow.ToString(TimeFormat, CultureInfo.InvariantCulture),
                sha256);

            var metaBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(meta, JsonOptions));
            using (var fs = new FileStream(metaTmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(metaBytes);
                fs.Flush(true);
            }

            File.Move(dbTmp, dbPath, overwrite: true);
            File.Move(metaTmp, metaPath, overwrite: true);
        }
        catch
        {
            DeleteSqliteFiles(dbTmp);
            TryDeleteFile(metaTmp);
            throw;
        }
    }

    /// <summary>
    /// Validates <c>snapshot.db</c> and <c>snapshot.meta.json</c> in <paramref name="snapshotDir"/>.
    /// Returns <c>null</c> without throwing if the snapshot is missing, corrupt, or ahead of <paramref name="opLogLastSeq"/>.
    /// </summary>
    public static ValidSnapshot? TryLoadValidSnapshot(string snapshotDir, long opLogLastSeq)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(snapshotDir)) return null;

            var dbPath = Path.Combine(snapshotDir, SnapshotDbFileName);
            var metaPath = Path.Combine(snapshotDir, SnapshotMetaFileName);
            if (!File.Exists(dbPath) || !File.Exists(metaPath)) return null;

            var metaJson = File.ReadAllText(metaPath, Encoding.UTF8);
            var meta = JsonSerializer.Deserialize<SnapshotMeta>(metaJson, JsonOptions);
            if (meta is null) return null;
            if (meta.LastSeq < 0 || meta.LastSeq > opLogLastSeq) return null;
            if (string.IsNullOrWhiteSpace(meta.Sha256) || meta.Sha256.Length != 64) return null;

            var actualHash = ComputeSha256Hex(dbPath);
            if (!string.Equals(actualHash, meta.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var dbSeq = TryReadSqliteSeq(dbPath);
            if (dbSeq is null || dbSeq.Value != meta.LastSeq)
            {
                return null;
            }

            return new ValidSnapshot(dbPath, metaPath, meta.LastSeq, meta.CreatedAt ?? string.Empty, actualHash);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Restores <paramref name="snapshotDbPath"/> to <paramref name="targetSqlitePath"/> cleanly and atomically.
    /// Copies and fsyncs the temporary file before replacing the target and removing stale sidecars.
    /// </summary>
    public static void RestoreSnapshot(string snapshotDbPath, string targetSqlitePath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(targetSqlitePath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmpPath = targetSqlitePath + ".restore.tmp";
        try
        {
            TryDeleteFile(tmpPath);
            File.Copy(snapshotDbPath, tmpPath, overwrite: true);
            using (var fs = new FileStream(tmpPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                fs.Flush(true);
            }

            DeleteSqliteSidecars(targetSqlitePath);
            File.Move(tmpPath, targetSqlitePath, overwrite: true);
        }
        catch
        {
            TryDeleteFile(tmpPath);
            throw;
        }
    }

    /// <summary>
    /// Attempts to read <c>applied_seq</c> from an existing SQLite file read-only.
    /// Returns <c>null</c> if the file is missing, unreadable, or corrupt.
    /// </summary>
    public static long? TryReadSqliteSeq(string sqlitePath)
    {
        if (!File.Exists(sqlitePath)) return null;

        try
        {
            var connStr = new SqliteConnectionStringBuilder
            {
                DataSource = sqlitePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString();

            using var conn = new SqliteConnection(connStr);
            conn.Open();

            using (var checkCmd = conn.CreateCommand())
            {
                checkCmd.CommandText = "PRAGMA quick_check;";
                var checkResult = checkCmd.ExecuteScalar() as string;
                if (!string.Equals(checkResult, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT seq FROM applied_seq WHERE id = 1;";
            var result = cmd.ExecuteScalar();
            return result is long seq && seq >= 0 ? seq : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Deletes a SQLite database file and its WAL, SHM, and rollback journal sidecars if present.</summary>
    public static void DeleteSqliteFiles(string sqlitePath)
    {
        TryDeleteFile(sqlitePath);
        DeleteSqliteSidecars(sqlitePath);
    }

    private static void DeleteSqliteSidecars(string sqlitePath)
    {
        TryDeleteFile(sqlitePath + "-wal");
        TryDeleteFile(sqlitePath + "-shm");
        TryDeleteFile(sqlitePath + "-journal");
    }

    private static string ComputeSha256Hex(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
