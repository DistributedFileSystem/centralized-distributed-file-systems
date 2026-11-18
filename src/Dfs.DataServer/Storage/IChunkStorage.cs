namespace Dfs.DataServer.Storage;

/// <summary>Result of a chunk write operation.</summary>
/// <param name="Size">Size of the chunk stored in bytes.</param>
/// <param name="Sha256">Lower-case hex SHA-256 checksum computed by the storage.</param>
public sealed record WriteChunkResult(long Size, string Sha256);

/// <summary>Result of a chunk read operation.</summary>
/// <param name="Data">The raw chunk bytes.</param>
/// <param name="Size">Size in bytes.</param>
/// <param name="Sha256">Lower-case hex SHA-256 checksum.</param>
public sealed record ReadChunkResult(byte[] Data, long Size, string Sha256);

/// <summary>Information about a stored chunk.</summary>
/// <param name="ChunkId">Id of the chunk.</param>
/// <param name="Size">Size in bytes.</param>
/// <param name="Sha256">Lower-case hex SHA-256 checksum.</param>
public sealed record StoredChunkInfo(string ChunkId, long Size, string Sha256);

/// <summary>Contract for managing chunk files and the local chunk index on a Data Server.</summary>
public interface IChunkStorage
{
    /// <summary>Directory storing chunks.</summary>
    string ChunksDir { get; }

    /// <summary>Writes chunk data atomically via temp file + rename and updates local index.</summary>
    Task<WriteChunkResult> WriteChunkAsync(string chunkId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    /// <summary>Reads raw chunk bytes and computes/verifies SHA-256.</summary>
    Task<ReadChunkResult> ReadChunkAsync(string chunkId, CancellationToken cancellationToken = default);

    /// <summary>Deletes chunks from disk and local index. Missing chunks are ignored.</summary>
    Task<int> DeleteChunksAsync(IEnumerable<string> chunkIds, CancellationToken cancellationToken = default);

    /// <summary>Gets the total number of chunks stored.</summary>
    Task<int> GetChunkCountAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets free bytes available for chunk storage.</summary>
    Task<long> GetFreeBytesAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets total capacity in bytes for chunk storage.</summary>
    Task<long> GetCapacityBytesAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists all chunks currently indexed on this Data Server.</summary>
    Task<IReadOnlyList<StoredChunkInfo>> ListChunksAsync(CancellationToken cancellationToken = default);
}
