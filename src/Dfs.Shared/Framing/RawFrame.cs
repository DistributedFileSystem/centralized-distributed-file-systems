using Dfs.Shared.Messages;

namespace Dfs.Shared.Framing;

/// <summary>Represents a raw binary frame on the wire.</summary>
/// <param name="Type">The 1-byte message type identifier.</param>
/// <param name="Payload">The payload byte array (excluding the 5-byte header).</param>
public sealed record RawFrame(MessageType Type, byte[] Payload)
{
    /// <summary>Payload length in bytes.</summary>
    public int Length => Payload.Length;
}

