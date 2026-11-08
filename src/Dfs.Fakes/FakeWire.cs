using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dfs.Shared.Messages;

namespace Dfs.Fakes;

internal static class FakeWire
{
    private const int MaxPayload = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal static T Parse<T>(byte[] payload)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(payload, Json)
                ?? throw new DfsException(ErrorCode.BadRequest, "Missing message body.");
        }
        catch (JsonException)
        {
            throw new DfsException(ErrorCode.BadRequest, "Malformed message body.");
        }
    }

    internal static async Task<(MessageType Type, byte[] Payload)?> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[5];
        if (await stream.ReadAsync(header.AsMemory(0, 1), cancellationToken) == 0) return null;
        await stream.ReadExactlyAsync(header.AsMemory(1), cancellationToken);
        var length = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (length > MaxPayload) throw new DfsException(ErrorCode.BadRequest, "Frame is too large.");
        var payload = new byte[(int)length];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return ((MessageType)header[4], payload);
    }

    internal static async Task WriteAsync(Stream stream, MessageType type, object message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, Json);
        await WriteFrameAsync(stream, type, payload, cancellationToken);
    }

    internal static async Task WriteChunkAsync(Stream stream, ReadChunkResponse response, byte[] bytes, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(response, Json);
        var payload = new byte[4 + json.Length + bytes.Length];
        BinaryPrimitives.WriteInt32BigEndian(payload, json.Length);
        json.CopyTo(payload, 4);
        bytes.CopyTo(payload, 4 + json.Length);
        await WriteFrameAsync(stream, MessageType.ReadChunkResponse, payload, cancellationToken);
    }

    internal static (T Header, byte[] Bytes) ParseChunk<T>(byte[] payload)
    {
        if (payload.Length < 4) throw new DfsException(ErrorCode.BadRequest, "Missing chunk header.");
        var length = BinaryPrimitives.ReadInt32BigEndian(payload);
        if (length < 0 || length > payload.Length - 4)
            throw new DfsException(ErrorCode.BadRequest, "Invalid chunk header length.");
        var header = Parse<T>(payload.AsSpan(4, length).ToArray());
        return (header, payload[(4 + length)..]);
    }

    private static async Task WriteFrameAsync(Stream stream, MessageType type, byte[] payload, CancellationToken cancellationToken)
    {
        if (payload.Length > MaxPayload) throw new DfsException(ErrorCode.BadRequest, "Frame is too large.");
        var header = new byte[5];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)payload.Length);
        header[4] = (byte)type;
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
    }
}
