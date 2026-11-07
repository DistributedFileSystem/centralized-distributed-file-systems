using Dfs.Shared.Messages;

namespace Dfs.Shared.Commands;

/// <summary>Chunk id and locations decided before <see cref="BeginUpload"/> is logged.</summary>
/// <param name="Index">Zero-based index of the chunk in the file.</param>
/// <param name="ChunkId">Id of the chunk.</param>
/// <param name="Servers">Addresses of the Data Servers chosen to hold the chunk (always a list).</param>
public sealed record PlannedChunk(int Index, string ChunkId, ValueList<string> Servers);
