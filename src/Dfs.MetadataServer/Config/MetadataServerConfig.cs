namespace Dfs.MetadataServer.Config;

/// <summary>
/// Configuration for the Metadata Server loaded from environment variables
/// as specified in docs/deployment.md.
/// </summary>
public sealed record MetadataServerConfig(
    string NodeRole,
    string NodeId,
    int ListenPort,
    string AdvertiseHost,
    IReadOnlyList<string> MetadataEndpoints,
    string DataDir,
    string TokenKey,
    string AdminUser,
    string AdminPassword)
{
    /// <summary>Default port for the Metadata Server.</summary>
    public const int DefaultPort = 7000;

    /// <summary>Path to the SQLite database file inside <see cref="DataDir"/>.</summary>
    public string DatabasePath => Path.Combine(DataDir, "metadata.db");

    /// <summary>Path to the append-only op log file inside <see cref="DataDir"/>.</summary>
    public string OpLogPath => Path.Combine(DataDir, "oplog.jsonl");

    /// <summary>
    /// Loads configuration from environment variables with sensible defaults.
    /// </summary>
    public static MetadataServerConfig FromEnvironment()
    {
        var nodeRole = Environment.GetEnvironmentVariable("NODE_ROLE");
        if (string.IsNullOrWhiteSpace(nodeRole)) nodeRole = "metadata";

        var nodeId = Environment.GetEnvironmentVariable("NODE_ID");
        if (string.IsNullOrWhiteSpace(nodeId)) nodeId = "m1";

        var portStr = Environment.GetEnvironmentVariable("LISTEN_PORT");
        var listenPort = int.TryParse(portStr, out var p) && p is >= 0 and <= 65535 ? p : DefaultPort;

        var advertiseHost = Environment.GetEnvironmentVariable("ADVERTISE_HOST");
        if (string.IsNullOrWhiteSpace(advertiseHost)) advertiseHost = "127.0.0.1";

        var endpointsStr = Environment.GetEnvironmentVariable("METADATA_ENDPOINTS");
        var metadataEndpoints = string.IsNullOrWhiteSpace(endpointsStr)
            ? new[] { $"{advertiseHost}:{listenPort}" }
            : endpointsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var dataDir = Environment.GetEnvironmentVariable("DATA_DIR");
        if (string.IsNullOrWhiteSpace(dataDir)) dataDir = "data";

        var tokenKey = Environment.GetEnvironmentVariable("TOKEN_KEY") ?? string.Empty;
        var adminUser = Environment.GetEnvironmentVariable("ADMIN_USER");
        if (string.IsNullOrWhiteSpace(adminUser)) adminUser = "admin";

        var adminPassword = Environment.GetEnvironmentVariable("ADMIN_PASSWORD") ?? string.Empty;

        return new MetadataServerConfig(
            nodeRole,
            nodeId,
            listenPort,
            advertiseHost,
            metadataEndpoints,
            dataDir,
            tokenKey,
            adminUser,
            adminPassword);
    }
}

