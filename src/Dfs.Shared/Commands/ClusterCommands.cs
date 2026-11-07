namespace Dfs.Shared.Commands;

/// <summary>Register a Data Server or update its address.</summary>
/// <param name="NodeId">Id of the Data Server.</param>
/// <param name="Address">ADVERTISE_HOST:PORT of the Data Server.</param>
/// <param name="CapacityBytes">Total capacity in bytes.</param>
/// <param name="FreeBytes">Free bytes.</param>
/// <param name="At">UTC time decided before logging.</param>
public sealed record RegisterDataServer(string NodeId, string Address, long CapacityBytes, long FreeBytes, DateTime At) : IMetadataCommand;

/// <summary>Record a Data Server heartbeat.</summary>
/// <param name="NodeId">Id of the Data Server.</param>
/// <param name="FreeBytes">Free bytes.</param>
/// <param name="ChunkCount">Number of chunks stored.</param>
/// <param name="At">UTC time decided before logging.</param>
public sealed record UpdateHeartbeat(string NodeId, long FreeBytes, int ChunkCount, DateTime At) : IMetadataCommand;
