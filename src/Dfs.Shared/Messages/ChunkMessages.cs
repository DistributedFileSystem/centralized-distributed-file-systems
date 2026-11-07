namespace Dfs.Shared.Messages;

/// <summary>Header of a chunk write (0x40); the raw bytes follow in the chunk framing.</summary>
/// <param name="RequestId">Correlation id (GUID string).</param>
/// <param name="ChunkId">Id of the chunk.</param>
/// <param name="Token">Write token.</param>
/// <param name="Size">Number of raw bytes that follow.</param>
public sealed record WriteChunk(string RequestId, string ChunkId, string Token, long Size);

/// <summary>Response to <see cref="WriteChunk"/> (0xC0).</summary>
/// <param name="RequestId">Correlation id of the request.</param>
/// <param name="ChunkId">Id of the chunk.</param>
/// <param name="Size">Number of bytes stored.</param>
/// <param name="Sha256">Lower-case hex SHA-256 computed by the Data Server.</param>
public sealed record WriteChunkResponse(string RequestId, string ChunkId, long Size, string Sha256);

/// <summary>Read a chunk (0x41).</summary>
/// <param name="RequestId">Correlation id (GUID string).</param>
/// <param name="ChunkId">Id of the chunk.</param>
/// <param name="Token">Read token.</param>
public sealed record ReadChunk(string RequestId, string ChunkId, string Token);

/// <summary>Header of the chunk read response (0xC1); the raw bytes follow in the chunk framing.</summary>
/// <param name="RequestId">Correlation id of the request.</param>
/// <param name="ChunkId">Id of the chunk.</param>
/// <param name="Size">Number of raw bytes that follow.</param>
/// <param name="Sha256">Lower-case hex SHA-256 of the chunk.</param>
public sealed record ReadChunkResponse(string RequestId, string ChunkId, long Size, string Sha256);

/// <summary>Metadata Server asks a Data Server to delete chunks (0x42).</summary>
/// <param name="RequestId">Correlation id (GUID string).</param>
/// <param name="ChunkIds">Ids of the chunks to delete.</param>
public sealed record DeleteChunk(string RequestId, ValueList<string> ChunkIds);

/// <summary>Response to <see cref="DeleteChunk"/> (0xC2).</summary>
/// <param name="RequestId">Correlation id of the request.</param>
/// <param name="Deleted">Number of chunks deleted.</param>
public sealed record DeleteChunkResponse(string RequestId, int Deleted);
