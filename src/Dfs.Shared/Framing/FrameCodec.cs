using System.Buffers.Binary;
using Dfs.Shared.Messages;

namespace Dfs.Shared.Framing;

/// <summary>
/// Encodes and decodes frames according to the DFS TCP wire protocol specified in docs/protocol.md:
/// 4-byte big-endian length + 1-byte message type + payload up to 16 MB.
/// </summary>
public static class FrameCodec
{
    /// <summary>
    /// Reads a raw frame from the stream.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The raw frame, or <c>null</c> if the stream closed cleanly before any byte was read.</returns>
    /// <exception cref="EndOfStreamException">Thrown if the stream ends prematurely mid-frame.</exception>
    /// <exception cref="DfsException">Thrown with ErrorCode.BadRequest if the payload exceeds maximum allowed size.</exception>
    public static async Task<RawFrame?> ReadRawFrameAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var header = new byte[ProtocolConstants.FrameHeaderLength];
        var firstByteRead = await stream.ReadAsync(header.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
        if (firstByteRead == 0)
        {
            return null;
        }

        await stream.ReadExactlyAsync(header.AsMemory(1, 4), cancellationToken).ConfigureAwait(false);
        var payloadLength = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0, 4));
        if (payloadLength > ProtocolConstants.MaxPayloadLength)
        {
            throw new DfsException(
                ErrorCode.BadRequest,
                $"Frame payload length {payloadLength} bytes exceeds the maximum limit of {ProtocolConstants.MaxPayloadLength} bytes.");
        }

        var payload = new byte[payloadLength];
        if (payloadLength > 0)
        {
            await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        }

        var messageType = (MessageType)header[4];
        return new RawFrame(messageType, payload);
    }

    /// <summary>
    /// Writes a raw frame to the stream.
    /// </summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="type">The 1-byte message type identifier.</param>
    /// <param name="payload">The frame payload bytes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="DfsException">Thrown with ErrorCode.BadRequest if payload exceeds maximum size.</exception>
    public static async Task WriteRawFrameAsync(Stream stream, MessageType type, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        if (payload.Length > ProtocolConstants.MaxPayloadLength)
        {
            throw new DfsException(
                ErrorCode.BadRequest,
                $"Frame payload length {payload.Length} bytes exceeds the maximum limit of {ProtocolConstants.MaxPayloadLength} bytes.");
        }

        var header = new byte[ProtocolConstants.FrameHeaderLength];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), (uint)payload.Length);
        header[4] = (byte)type;

        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        if (!payload.IsEmpty)
        {
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        }

        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads and deserializes a JSON control message from the stream.
    /// </summary>
    /// <typeparam name="T">The message record type.</typeparam>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="expectedType">The expected message type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized message, or <c>null</c> if the stream closed cleanly.</returns>
    /// <exception cref="DfsException">
    /// Thrown if an ErrorResponse is received, or if the received message type does not match the expected type,
    /// or if deserialization fails.
    /// </exception>
    public static async Task<T?> ReadMessageAsync<T>(Stream stream, MessageType expectedType, CancellationToken cancellationToken = default)
        where T : class
    {
        var frame = await ReadRawFrameAsync(stream, cancellationToken).ConfigureAwait(false);
        if (frame is null)
        {
            return null;
        }

        if (frame.Type == MessageType.Error)
        {
            var error = FrameSerialization.DeserializeJson<ErrorResponse>(frame.Payload);
            throw new DfsException(error.Code, error.Message, error.Details);
        }

        if (frame.Type != expectedType)
        {
            throw new DfsException(
                ErrorCode.BadRequest,
                $"Expected message type {expectedType} (0x{(byte)expectedType:X2}) but received {frame.Type} (0x{(byte)frame.Type:X2}).");
        }

        return FrameSerialization.DeserializeJson<T>(frame.Payload);
    }

    /// <summary>
    /// Serializes and writes a JSON control message to the stream.
    /// </summary>
    /// <typeparam name="T">The message record type.</typeparam>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="type">The message type identifier.</param>
    /// <param name="message">The message instance to serialize.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task WriteMessageAsync<T>(Stream stream, MessageType type, T message, CancellationToken cancellationToken = default)
    {
        var payload = FrameSerialization.SerializeJson(message);
        await WriteRawFrameAsync(stream, type, payload, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads a chunk frame (4-byte header length + JSON header + raw chunk bytes).
    /// </summary>
    /// <typeparam name="THeader">The chunk header type (e.g. WriteChunk or ReadChunkResponse).</typeparam>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="expectedType">The expected chunk message type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The chunk frame with parsed header and raw data, or <c>null</c> if stream closed cleanly.</returns>
    public static async Task<ChunkFrame<THeader>?> ReadChunkFrameAsync<THeader>(Stream stream, MessageType expectedType, CancellationToken cancellationToken = default)
        where THeader : class
    {
        var frame = await ReadRawFrameAsync(stream, cancellationToken).ConfigureAwait(false);
        if (frame is null)
        {
            return null;
        }

        if (frame.Type == MessageType.Error)
        {
            var error = FrameSerialization.DeserializeJson<ErrorResponse>(frame.Payload);
            throw new DfsException(error.Code, error.Message, error.Details);
        }

        if (frame.Type != expectedType)
        {
            throw new DfsException(
                ErrorCode.BadRequest,
                $"Expected chunk message type {expectedType} (0x{(byte)expectedType:X2}) but received {frame.Type} (0x{(byte)frame.Type:X2}).");
        }

        return DecodeChunkPayload<THeader>(frame.Payload);
    }

    /// <summary>
    /// Serializes and writes a chunk frame (4-byte header length + JSON header + raw chunk bytes).
    /// </summary>
    /// <typeparam name="THeader">The chunk header type (e.g. WriteChunk or ReadChunkResponse).</typeparam>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="type">The chunk message type.</param>
    /// <param name="header">The chunk header record.</param>
    /// <param name="chunkBytes">The raw chunk bytes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task WriteChunkFrameAsync<THeader>(
        Stream stream,
        MessageType type,
        THeader header,
        ReadOnlyMemory<byte> chunkBytes,
        CancellationToken cancellationToken = default)
    {
        var payload = EncodeChunkPayload(header, chunkBytes.Span);
        await WriteRawFrameAsync(stream, type, payload, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Encodes a raw frame into an in-memory byte array (5-byte header + payload).
    /// </summary>
    /// <param name="type">The message type.</param>
    /// <param name="payload">The frame payload.</param>
    /// <returns>The complete wire frame byte array.</returns>
    public static byte[] EncodeRawFrame(MessageType type, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > ProtocolConstants.MaxPayloadLength)
        {
            throw new DfsException(
                ErrorCode.BadRequest,
                $"Frame payload length {payload.Length} bytes exceeds the maximum limit of {ProtocolConstants.MaxPayloadLength} bytes.");
        }

        var frame = new byte[ProtocolConstants.FrameHeaderLength + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(0, 4), (uint)payload.Length);
        frame[4] = (byte)type;
        payload.CopyTo(frame.AsSpan(ProtocolConstants.FrameHeaderLength));
        return frame;
    }

    /// <summary>
    /// Decodes an in-memory frame buffer into message type and payload.
    /// </summary>
    /// <param name="frameBytes">The complete wire frame bytes.</param>
    /// <returns>A tuple of message type and payload byte array.</returns>
    public static (MessageType Type, byte[] Payload) DecodeRawFrame(ReadOnlySpan<byte> frameBytes)
    {
        if (frameBytes.Length < ProtocolConstants.FrameHeaderLength)
        {
            throw new DfsException(ErrorCode.BadRequest, "Frame byte buffer is shorter than the 5-byte header.");
        }

        var payloadLength = BinaryPrimitives.ReadUInt32BigEndian(frameBytes.Slice(0, 4));
        if (payloadLength > ProtocolConstants.MaxPayloadLength)
        {
            throw new DfsException(
                ErrorCode.BadRequest,
                $"Frame payload length {payloadLength} bytes exceeds the maximum limit of {ProtocolConstants.MaxPayloadLength} bytes.");
        }

        if (frameBytes.Length != ProtocolConstants.FrameHeaderLength + payloadLength)
        {
            throw new DfsException(
                ErrorCode.BadRequest,
                $"Frame buffer length ({frameBytes.Length}) does not match expected length ({ProtocolConstants.FrameHeaderLength + payloadLength}).");
        }

        var type = (MessageType)frameBytes[4];
        var payload = frameBytes.Slice(ProtocolConstants.FrameHeaderLength, (int)payloadLength).ToArray();
        return (type, payload);
    }

    /// <summary>
    /// Encodes a chunk message header and raw chunk bytes into a combined chunk payload.
    /// </summary>
    /// <typeparam name="THeader">The header type.</typeparam>
    /// <param name="header">The header record.</param>
    /// <param name="chunkBytes">The raw chunk data bytes.</param>
    /// <returns>The combined chunk payload (4-byte header length + JSON + chunk data).</returns>
    public static byte[] EncodeChunkPayload<THeader>(THeader header, ReadOnlySpan<byte> chunkBytes)
    {
        var headerBytes = FrameSerialization.SerializeJson(header);
        var totalLength = checked(4 + headerBytes.Length + chunkBytes.Length);
        if (totalLength > ProtocolConstants.MaxPayloadLength)
        {
            throw new DfsException(ErrorCode.BadRequest, "Chunk payload exceeds maximum size limit.");
        }

        var payload = new byte[totalLength];
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0, 4), headerBytes.Length);
        headerBytes.CopyTo(payload.AsSpan(4));
        chunkBytes.CopyTo(payload.AsSpan(4 + headerBytes.Length));
        return payload;
    }

    /// <summary>
    /// Decodes a combined chunk payload into header record and raw chunk data.
    /// </summary>
    /// <typeparam name="THeader">The header type.</typeparam>
    /// <param name="payload">The combined chunk payload bytes.</param>
    /// <returns>A ChunkFrame containing the deserialized header and chunk data.</returns>
    public static ChunkFrame<THeader> DecodeChunkPayload<THeader>(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length < 4)
        {
            throw new DfsException(ErrorCode.BadRequest, "Chunk payload is missing the 4-byte header length.");
        }

        var headerLength = BinaryPrimitives.ReadInt32BigEndian(payload.Span.Slice(0, 4));
        if (headerLength < 0 || headerLength > payload.Length - 4)
        {
            throw new DfsException(ErrorCode.BadRequest, $"Invalid chunk header length {headerLength}.");
        }

        var headerSpan = payload.Span.Slice(4, headerLength);
        var header = FrameSerialization.DeserializeJson<THeader>(headerSpan);
        var chunkBytes = payload.Slice(4 + headerLength);
        return new ChunkFrame<THeader>(header, chunkBytes);
    }
}
