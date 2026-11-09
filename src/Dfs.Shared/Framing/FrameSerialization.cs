using System.Text.Json;
using System.Text.Json.Serialization;
using Dfs.Shared.Messages;

namespace Dfs.Shared.Framing;

/// <summary>JSON serialization configuration and helpers for DFS protocol messages.</summary>
public static class FrameSerialization
{
    /// <summary>Standard JSON serializer options for protocol control messages (camelCase, nulls omitted).</summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Serializes an object into UTF-8 JSON bytes.</summary>
    /// <typeparam name="T">The type of the object.</typeparam>
    /// <param name="message">The object to serialize.</param>
    /// <returns>A UTF-8 encoded byte array.</returns>
    public static byte[] SerializeJson<T>(T message) =>
        JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);

    /// <summary>Deserializes a JSON payload into the specified type.</summary>
    /// <typeparam name="T">The type to deserialize into.</typeparam>
    /// <param name="jsonBytes">The UTF-8 JSON bytes.</param>
    /// <returns>The deserialized object instance.</returns>
    /// <exception cref="DfsException">Thrown with ErrorCode.BadRequest if deserialization fails or returns null.</exception>
    public static T DeserializeJson<T>(ReadOnlySpan<byte> jsonBytes)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(jsonBytes, JsonOptions)
                ?? throw new DfsException(ErrorCode.BadRequest, "Missing message body.");
        }
        catch (JsonException ex)
        {
            throw new DfsException(ErrorCode.BadRequest, "Malformed JSON message body: " + ex.Message);
        }
    }
}

