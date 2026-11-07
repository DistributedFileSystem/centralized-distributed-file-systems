namespace Dfs.Shared.Messages;

/// <summary>Data Server registers with the Metadata Server (0x50).</summary>
/// <param name="RequestId">Correlation id (GUID string).</param>
/// <param name="NodeId">Id of the Data Server.</param>
/// <param name="Address">ADVERTISE_HOST:PORT of the Data Server.</param>
/// <param name="CapacityBytes">Total capacity in bytes.</param>
/// <param name="FreeBytes">Free bytes.</param>
public sealed record Register(string RequestId, string NodeId, string Address, long CapacityBytes, long FreeBytes);

/// <summary>Response to <see cref="Register"/> (0xD0).</summary>
/// <param name="RequestId">Correlation id of the request.</param>
/// <param name="HeartbeatIntervalSeconds">Seconds between heartbeats.</param>
public sealed record RegisterResponse(string RequestId, int HeartbeatIntervalSeconds);

/// <summary>Data Server heartbeat, every 3 seconds (0x51).</summary>
/// <param name="RequestId">Correlation id (GUID string).</param>
/// <param name="NodeId">Id of the Data Server.</param>
/// <param name="FreeBytes">Free bytes.</param>
/// <param name="ChunkCount">Number of chunks stored.</param>
public sealed record Heartbeat(string RequestId, string NodeId, long FreeBytes, int ChunkCount);

/// <summary>Response to <see cref="Heartbeat"/> (0xD1).</summary>
/// <param name="RequestId">Correlation id of the request.</param>
public sealed record HeartbeatResponse(string RequestId);

/// <summary>Data Server reports the chunks it holds (0x52).</summary>
/// <param name="RequestId">Correlation id (GUID string).</param>
/// <param name="NodeId">Id of the Data Server.</param>
/// <param name="Chunks">Chunks held.</param>
public sealed record ChunkReport(string RequestId, string NodeId, ValueList<ChunkInfo> Chunks);

/// <summary>Response to <see cref="ChunkReport"/> (0xD2).</summary>
/// <param name="RequestId">Correlation id of the request.</param>
/// <param name="UnknownChunkIds">Chunks the Data Server holds but the Metadata Server does not know.</param>
public sealed record ChunkReportResponse(string RequestId, ValueList<string> UnknownChunkIds);
