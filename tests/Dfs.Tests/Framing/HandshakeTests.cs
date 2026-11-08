using System.Buffers;
using System.IO.Pipelines;
using Dfs.Shared.Framing;
using Dfs.Shared.Messages;

namespace Dfs.Tests.Framing;

public class HandshakeTests
{
    private sealed class DuplexStream : Stream
    {
        private readonly PipeReader _reader;
        private readonly PipeWriter _writer;

        public DuplexStream(PipeReader reader, PipeWriter writer)
        {
            _reader = reader;
            _writer = writer;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => _writer.FlushAsync().GetAwaiter().GetResult();
        public override Task FlushAsync(CancellationToken cancellationToken) => _writer.FlushAsync(cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).GetAwaiter().GetResult();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var result = await _reader.ReadAsync(cancellationToken);
            var length = (int)Math.Min(buffer.Length, result.Buffer.Length);
            result.Buffer.Slice(0, length).CopyTo(buffer.Span);
            _reader.AdvanceTo(result.Buffer.GetPosition(length));
            return length;
        }

        public override void Write(byte[] buffer, int offset, int count) =>
            WriteAsync(buffer.AsMemory(offset, count)).GetAwaiter().GetResult();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await _writer.WriteAsync(buffer, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            _writer.Complete();
            _reader.Complete();
            base.Dispose(disposing);
        }
    }

    private static (Stream ClientStream, Stream ServerStream) CreateConnectedStreams()
    {
        var clientPipe = new Pipe();
        var serverPipe = new Pipe();
        var clientStream = new DuplexStream(serverPipe.Reader, clientPipe.Writer);
        var serverStream = new DuplexStream(clientPipe.Reader, serverPipe.Writer);
        return (clientStream, serverStream);
    }

    [Fact]
    public async Task Handshake_Success_RoundTrip()
    {
        var (clientStream, serverStream) = CreateConnectedStreams();
        await using (clientStream)
        await using (serverStream)
        {
            var clientHello = new Hello(Guid.NewGuid().ToString("D"), 1, NodeType.Client);

            var serverTask = Task.Run(async () =>
            {
                var receivedHello = await HandshakeHandler.ServerHandshakeAsync(serverStream, "m1", NodeType.Metadata);
                return receivedHello;
            });

            var clientTask = Task.Run(async () =>
            {
                var response = await HandshakeHandler.ClientHandshakeAsync(clientStream, clientHello);
                return response;
            });

            var receivedHello = await serverTask;
            var response = await clientTask;

            Assert.NotNull(receivedHello);
            Assert.Equal(clientHello.RequestId, receivedHello.RequestId);
            Assert.Equal(1, receivedHello.ProtocolVersion);
            Assert.Equal(NodeType.Client, receivedHello.NodeType);

            Assert.NotNull(response);
            Assert.Equal(clientHello.RequestId, response.RequestId);
            Assert.Equal(1, response.ProtocolVersion);
            Assert.Equal(NodeType.Metadata, response.NodeType);
            Assert.Equal("m1", response.NodeId);
        }
    }

    [Fact]
    public async Task Handshake_VersionMismatch_ServerRejectsAndRepliesError()
    {
        var (clientStream, serverStream) = CreateConnectedStreams();
        await using (clientStream)
        await using (serverStream)
        {
            var wrongVersionHello = new Hello(Guid.NewGuid().ToString("D"), 99, NodeType.Client);

            var serverTask = Task.Run(async () =>
            {
                var ex = await Assert.ThrowsAsync<DfsException>(() =>
                    HandshakeHandler.ServerHandshakeAsync(serverStream, "m1", NodeType.Metadata));
                Assert.Equal(ErrorCode.VersionMismatch, ex.Code);
            });

            var clientTask = Task.Run(async () =>
            {
                var ex = await Assert.ThrowsAsync<DfsException>(() =>
                    HandshakeHandler.ClientHandshakeAsync(clientStream, wrongVersionHello));
                Assert.Equal(ErrorCode.VersionMismatch, ex.Code);
            });

            await Task.WhenAll(serverTask, clientTask);
        }
    }

    [Fact]
    public async Task Handshake_MessageBeforeHello_ServerRejectsWithBadRequest()
    {
        var (clientStream, serverStream) = CreateConnectedStreams();
        await using (clientStream)
        await using (serverStream)
        {
            var loginBeforeHello = new Login(Guid.NewGuid().ToString("D"), "an.nguyen", "secret");

            var serverTask = Task.Run(async () =>
            {
                var ex = await Assert.ThrowsAsync<DfsException>(() =>
                    HandshakeHandler.ServerHandshakeAsync(serverStream, "m1", NodeType.Metadata));
                Assert.Equal(ErrorCode.BadRequest, ex.Code);
            });

            var clientTask = Task.Run(async () =>
            {
                // Send Login instead of Hello
                await FrameCodec.WriteMessageAsync(clientStream, MessageType.Login, loginBeforeHello);

                // Now read server response, which must be ErrorResponse
                var responseFrame = await FrameCodec.ReadRawFrameAsync(clientStream);
                Assert.NotNull(responseFrame);
                Assert.Equal(MessageType.Error, responseFrame.Type);
                var error = FrameSerialization.DeserializeJson<ErrorResponse>(responseFrame.Payload);
                Assert.Equal(ErrorCode.BadRequest, error.Code);
            });

            await Task.WhenAll(serverTask, clientTask);
        }
    }

    [Fact]
    public async Task Handshake_MalformedHello_ServerRejectsWithBadRequest()
    {
        var (clientStream, serverStream) = CreateConnectedStreams();
        await using (clientStream)
        await using (serverStream)
        {
            var serverTask = Task.Run(async () =>
            {
                var ex = await Assert.ThrowsAsync<DfsException>(() =>
                    HandshakeHandler.ServerHandshakeAsync(serverStream, "m1", NodeType.Metadata));
                Assert.Equal(ErrorCode.BadRequest, ex.Code);
            });

            var clientTask = Task.Run(async () =>
            {
                // Send MessageType.Hello but with invalid JSON
                var invalidBytes = "not-json-content"u8.ToArray();
                await FrameCodec.WriteRawFrameAsync(clientStream, MessageType.Hello, invalidBytes);

                var responseFrame = await FrameCodec.ReadRawFrameAsync(clientStream);
                Assert.NotNull(responseFrame);
                Assert.Equal(MessageType.Error, responseFrame.Type);
                var error = FrameSerialization.DeserializeJson<ErrorResponse>(responseFrame.Payload);
                Assert.Equal(ErrorCode.BadRequest, error.Code);
            });

            await Task.WhenAll(serverTask, clientTask);
        }
    }

    [Fact]
    public async Task Handshake_CleanDisconnect_ServerReturnsNull()
    {
        using var emptyStream = new MemoryStream();
        var result = await HandshakeHandler.ServerHandshakeAsync(emptyStream, "m1", NodeType.Metadata);
        Assert.Null(result);
    }
}
