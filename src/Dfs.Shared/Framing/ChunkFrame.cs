namespace Dfs.Shared.Framing;

/// <summary>Represents a chunk message containing a structured JSON header and raw binary chunk data.</summary>
/// <typeparam name="THeader">The type of the chunk header (e.g., WriteChunk or ReadChunkResponse).</typeparam>
/// <param name="Header">The deserialized chunk header record.</param>
/// <param name="ChunkBytes">The raw bytes of the chunk.</param>
public sealed record ChunkFrame<THeader>(THeader Header, ReadOnlyMemory<byte> ChunkBytes);
