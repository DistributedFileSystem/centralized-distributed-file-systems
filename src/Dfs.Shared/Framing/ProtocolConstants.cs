namespace Dfs.Shared.Framing;

/// <summary>Wire protocol constants specified in docs/protocol.md.</summary>
public static class ProtocolConstants
{
    /// <summary>Current protocol version, which is 1 for stage 1.</summary>
    public const int CurrentProtocolVersion = 1;

    /// <summary>Maximum allowed frame payload size in bytes (16 MB).</summary>
    public const int MaxPayloadLength = 16 * 1024 * 1024;

    /// <summary>Length of the binary frame header in bytes (4-byte length + 1-byte message type).</summary>
    public const int FrameHeaderLength = 5;

    /// <summary>Default TCP listening port for Metadata Server (7000).</summary>
    public const int DefaultMetadataPort = 7000;

    /// <summary>Default TCP listening port for Data Server (7001).</summary>
    public const int DefaultDataPort = 7001;
}
