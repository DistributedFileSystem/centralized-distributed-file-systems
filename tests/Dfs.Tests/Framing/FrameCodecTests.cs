using System.Buffers.Binary;
using System.Text.Json;
using Dfs.Shared.Framing;
using Dfs.Shared.Messages;

namespace Dfs.Tests.Framing;

public class FrameCodecTests
{
    [Fact]
    public void EncodeDecodeRawFrame_RoundTrip()
    {
        var originalPayload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x42 };
        var encoded = FrameCodec.EncodeRawFrame(MessageType.Stat, originalPayload);

        Assert.Equal(ProtocolConstants.FrameHeaderLength + originalPayload.Length, encoded.Length);
        var (type, decodedPayload) = FrameCodec.DecodeRawFrame(encoded);

        Assert.Equal(MessageType.Stat, type);
        Assert.Equal(originalPayload, decodedPayload);
    }

    [Fact]
    public void EncodeDecodeRawFrame_EmptyPayload()
    {
        var encoded = FrameCodec.EncodeRawFrame(MessageType.LogoutResponse, Array.Empty<byte>());
        Assert.Equal(ProtocolConstants.FrameHeaderLength, encoded.Length);

        var (type, decodedPayload) = FrameCodec.DecodeRawFrame(encoded);
        Assert.Equal(MessageType.LogoutResponse, type);
        Assert.Empty(decodedPayload);
    }

    [Fact]
    public void DecodeRawFrame_TooShortForHeader_ThrowsBadRequest()
    {
        var invalid = new byte[] { 0x00, 0x00, 0x01 };
        var ex = Assert.Throws<DfsException>(() => FrameCodec.DecodeRawFrame(invalid));
        Assert.Equal(ErrorCode.BadRequest, ex.Code);
    }

    [Fact]
    public void DecodeRawFrame_LengthMismatch_ThrowsBadRequest()
    {
        var frame = new byte[ProtocolConstants.FrameHeaderLength + 5];
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(0, 4), 10); // claims 10 bytes payload, but only 5 provided
        frame[4] = (byte)MessageType.Stat;

        var ex = Assert.Throws<DfsException>(() => FrameCodec.DecodeRawFrame(frame));
        Assert.Equal(ErrorCode.BadRequest, ex.Code);
    }

    [Fact]
    public async Task StreamReadWrite_MultipleRawFrames_RoundTrip()
    {
        using var stream = new MemoryStream();

        var frame1 = new byte[] { 1, 2, 3, 4 };
        var frame2 = new byte[] { 5, 6, 7, 8, 9, 10 };

        await FrameCodec.WriteRawFrameAsync(stream, MessageType.Hello, frame1);
        await FrameCodec.WriteRawFrameAsync(stream, MessageType.Stat, frame2);

        stream.Position = 0;

        var read1 = await FrameCodec.ReadRawFrameAsync(stream);
        Assert.NotNull(read1);
        Assert.Equal(MessageType.Hello, read1.Type);
        Assert.Equal(frame1, read1.Payload);

        var read2 = await FrameCodec.ReadRawFrameAsync(stream);
        Assert.NotNull(read2);
        Assert.Equal(MessageType.Stat, read2.Type);
        Assert.Equal(frame2, read2.Payload);

        var read3 = await FrameCodec.ReadRawFrameAsync(stream);
        Assert.Null(read3); // Clean EOF
    }

    [Fact]
    public async Task ReadWriteMessage_ControlMessage_RoundTrip()
    {
        using var stream = new MemoryStream();
        var request = new Login(Guid.NewGuid().ToString("D"), "cuong.khong", "secret_pwd");

        await FrameCodec.WriteMessageAsync(stream, MessageType.Login, request);
        stream.Position = 0;

        var received = await FrameCodec.ReadMessageAsync<Login>(stream, MessageType.Login);
        Assert.NotNull(received);
        Assert.Equal(request.RequestId, received.RequestId);
        Assert.Equal(request.Username, received.Username);
        Assert.Equal(request.Password, received.Password);
    }

    [Fact]
    public async Task ReadMessage_UnexpectedType_ThrowsBadRequest()
    {
        using var stream = new MemoryStream();
        var request = new Logout(Guid.NewGuid().ToString("D"), "session-123");

        await FrameCodec.WriteMessageAsync(stream, MessageType.Logout, request);
        stream.Position = 0;

        var ex = await Assert.ThrowsAsync<DfsException>(() =>
            FrameCodec.ReadMessageAsync<Login>(stream, MessageType.Login));

        Assert.Equal(ErrorCode.BadRequest, ex.Code);
    }

    [Fact]
    public async Task ReadMessage_ErrorResponse_UnpacksAndThrowsDfsException()
    {
        using var stream = new MemoryStream();
        var details = new Dictionary<string, JsonElement>
        {
            ["currentVersion"] = JsonDocument.Parse("4").RootElement,
        };
        var error = new ErrorResponse(Guid.NewGuid().ToString("D"), ErrorCode.Conflict, "File modified", details);

        await FrameCodec.WriteMessageAsync(stream, MessageType.Error, error);
        stream.Position = 0;

        var ex = await Assert.ThrowsAsync<DfsException>(() =>
            FrameCodec.ReadMessageAsync<Login>(stream, MessageType.Login));

        Assert.Equal(ErrorCode.Conflict, ex.Code);
        Assert.Equal("File modified", ex.Message);
        Assert.NotNull(ex.Details);
        Assert.Equal(4, ex.Details["currentVersion"].GetInt32());
    }

    [Fact]
    public async Task ChunkFrame_WriteChunk_RoundTrip()
    {
        using var stream = new MemoryStream();
        var header = new WriteChunk(Guid.NewGuid().ToString("D"), "c-chunk01", "tok-write-123", 1024);
        var chunkBytes = new byte[1024];
        new Random(42).NextBytes(chunkBytes);

        await FrameCodec.WriteChunkFrameAsync(stream, MessageType.WriteChunk, header, chunkBytes);
        stream.Position = 0;

        var received = await FrameCodec.ReadChunkFrameAsync<WriteChunk>(stream, MessageType.WriteChunk);
        Assert.NotNull(received);
        Assert.Equal(header.RequestId, received.Header.RequestId);
        Assert.Equal(header.ChunkId, received.Header.ChunkId);
        Assert.Equal(header.Token, received.Header.Token);
        Assert.Equal(header.Size, received.Header.Size);
        Assert.True(chunkBytes.AsSpan().SequenceEqual(received.ChunkBytes.Span));
    }

    [Fact]
    public async Task ChunkFrame_ReadChunkResponse_RoundTrip()
    {
        using var stream = new MemoryStream();
        var header = new ReadChunkResponse(Guid.NewGuid().ToString("D"), "c-chunk02", 512, "9f86d081884c7d65");
        var chunkBytes = new byte[512];
        new Random(100).NextBytes(chunkBytes);

        await FrameCodec.WriteChunkFrameAsync(stream, MessageType.ReadChunkResponse, header, chunkBytes);
        stream.Position = 0;

        var received = await FrameCodec.ReadChunkFrameAsync<ReadChunkResponse>(stream, MessageType.ReadChunkResponse);
        Assert.NotNull(received);
        Assert.Equal(header.RequestId, received.Header.RequestId);
        Assert.Equal(header.ChunkId, received.Header.ChunkId);
        Assert.Equal(header.Size, received.Header.Size);
        Assert.Equal(header.Sha256, received.Header.Sha256);
        Assert.True(chunkBytes.AsSpan().SequenceEqual(received.ChunkBytes.Span));
    }

    [Fact]
    public async Task ReadChunkFrame_ErrorResponse_UnpacksAndThrowsDfsException()
    {
        using var stream = new MemoryStream();
        var error = new ErrorResponse(Guid.NewGuid().ToString("D"), ErrorCode.NotFound, "Chunk not found");

        await FrameCodec.WriteMessageAsync(stream, MessageType.Error, error);
        stream.Position = 0;

        var ex = await Assert.ThrowsAsync<DfsException>(() =>
            FrameCodec.ReadChunkFrameAsync<ReadChunkResponse>(stream, MessageType.ReadChunkResponse));

        Assert.Equal(ErrorCode.NotFound, ex.Code);
        Assert.Equal("Chunk not found", ex.Message);
    }

    [Fact]
    public async Task ReadRawFrame_PayloadExceeds16MB_ThrowsBadRequest()
    {
        using var stream = new MemoryStream();
        var header = new byte[5];
        // 16MB + 1 byte
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), ProtocolConstants.MaxPayloadLength + 1);
        header[4] = (byte)MessageType.Stat;
        await stream.WriteAsync(header);
        stream.Position = 0;

        var ex = await Assert.ThrowsAsync<DfsException>(() => FrameCodec.ReadRawFrameAsync(stream));
        Assert.Equal(ErrorCode.BadRequest, ex.Code);
    }

    [Fact]
    public async Task WriteRawFrame_PayloadExceeds16MB_ThrowsBadRequest()
    {
        using var stream = new MemoryStream();
        // create a dummy large memory without allocating 16MB by throwing via length check
        var ex = await Assert.ThrowsAsync<DfsException>(() =>
            FrameCodec.WriteRawFrameAsync(stream, MessageType.Stat, new byte[ProtocolConstants.MaxPayloadLength + 1]));

        Assert.Equal(ErrorCode.BadRequest, ex.Code);
    }

    [Fact]
    public async Task ReadRawFrame_TruncatedHeader_ThrowsEndOfStreamException()
    {
        using var stream = new MemoryStream(new byte[] { 0, 0, 1 }); // only 3 bytes
        await Assert.ThrowsAsync<EndOfStreamException>(() => FrameCodec.ReadRawFrameAsync(stream));
    }

    [Fact]
    public async Task ReadRawFrame_TruncatedPayload_ThrowsEndOfStreamException()
    {
        using var stream = new MemoryStream();
        var header = new byte[5];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), 100);
        header[4] = (byte)MessageType.Stat;
        await stream.WriteAsync(header);
        await stream.WriteAsync(new byte[20]); // only 20 bytes out of 100
        stream.Position = 0;

        await Assert.ThrowsAsync<EndOfStreamException>(() => FrameCodec.ReadRawFrameAsync(stream));
    }

    [Fact]
    public void DecodeChunkPayload_InvalidHeaderLength_ThrowsBadRequest()
    {
        var payload = new byte[10];
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0, 4), 20); // claims 20 bytes header, only 6 bytes left
        var ex = Assert.Throws<DfsException>(() => FrameCodec.DecodeChunkPayload<WriteChunk>(payload));
        Assert.Equal(ErrorCode.BadRequest, ex.Code);
    }

    [Fact]
    public void DecodeChunkPayload_NegativeHeaderLength_ThrowsBadRequest()
    {
        var payload = new byte[10];
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0, 4), -1);
        var ex = Assert.Throws<DfsException>(() => FrameCodec.DecodeChunkPayload<WriteChunk>(payload));
        Assert.Equal(ErrorCode.BadRequest, ex.Code);
    }
}
