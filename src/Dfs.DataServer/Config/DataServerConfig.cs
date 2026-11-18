namespace Dfs.DataServer.Config;

/// <summary>
/// Configuration for the Data Server loaded from environment variables
/// as specified in docs/deployment.md.
/// </summary>
public sealed record DataServerConfig(
    string NodeRole,
    string NodeId,
    int ListenPort,
    string AdvertiseHost,
    IReadOnlyList<string> MetadataEndpoints,
    string DataDir,
    string TokenKey,
    long CapacityBytes = 0)
{
    /// <summary>Default port for the Data Server.</summary>
    public const int DefaultPort = 7001;

    /// <summary>ADVERTISE_HOST:PORT formatted address used when registering with Metadata Server.</summary>
    public string AdvertiseAddress => $"{AdvertiseHost}:{ListenPort}";

    /// <summary>Directory storing chunk files inside <see cref="DataDir"/>.</summary>
    public string ChunksDir => Path.Combine(DataDir, "chunks");

    /// <summary>Path to the local SQLite index inside <see cref="DataDir"/>.</summary>
    public string IndexPath => Path.Combine(DataDir, "index.db");

    /// <summary>
    /// Loads configuration from environment variables with sensible defaults.
    /// </summary>
    public static DataServerConfig FromEnvironment()
    {
        var nodeRole = Environment.GetEnvironmentVariable("NODE_ROLE");
        if (string.IsNullOrWhiteSpace(nodeRole)) nodeRole = "data";

        var nodeId = Environment.GetEnvironmentVariable("NODE_ID");
        if (string.IsNullOrWhiteSpace(nodeId)) nodeId = "ds-1";

        var portStr = Environment.GetEnvironmentVariable("LISTEN_PORT");
        var listenPort = int.TryParse(portStr, out var p) && p is >= 0 and <= 65535 ? p : DefaultPort;

        var advertiseHost = Environment.GetEnvironmentVariable("ADVERTISE_HOST");
        if (string.IsNullOrWhiteSpace(advertiseHost)) advertiseHost = "127.0.0.1";

        var endpointsStr = Environment.GetEnvironmentVariable("METADATA_ENDPOINTS");
        var metadataEndpoints = string.IsNullOrWhiteSpace(endpointsStr)
            ? new[] { "127.0.0.1:7000" }
            : endpointsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var dataDir = Environment.GetEnvironmentVariable("DATA_DIR");
        if (string.IsNullOrWhiteSpace(dataDir)) dataDir = "data";

        var tokenKey = Environment.GetEnvironmentVariable("TOKEN_KEY") ?? string.Empty;

        var capStr = Environment.GetEnvironmentVariable("CAPACITY_BYTES");
        var capacityBytes = long.TryParse(capStr, out var cap) && cap > 0 ? cap : 0;

        return new DataServerConfig(
            nodeRole,
            nodeId,
            listenPort,
            advertiseHost,
            metadataEndpoints,
            dataDir,
            tokenKey,
            capacityBytes);
    }
}
