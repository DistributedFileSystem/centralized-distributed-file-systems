using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Dfs.Shared.Framing;
using Dfs.Shared.Messages;

namespace Dfs.AcceptanceTests;

/// <summary>
/// Acceptance tests for Issue #14 (M1-11): Framing, Hello handshake, and Version mismatch.
/// Verifies the wire protocol over real TCP sockets strictly according to docs/protocol.md.
/// </summary>
public class ProtocolAcceptanceTests
{
    private static (TcpListener Listener, int Port) StartLoopbackListener()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return (listener, port);
    }

    /// <summary>
    /// Test 1: First message is Hello. Handshake succeeds with valid HelloResponse and
    /// the TCP connection remains active for subsequent operations on the same socket.
    /// </summary>
    [Fact]
    public async Task Hello_Success_KeepsConnectionAliveForSubsequentFrames()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (listener, port) = StartLoopbackListener();

        try
        {
            var serverTask = Task.Run(async () =>
            {
                using var serverClient = await listener.AcceptTcpClientAsync(cts.Token);
                await using var stream = serverClient.GetStream();

                var hello = await HandshakeHandler.ServerHandshakeAsync(
                    stream,
                    serverNodeId: "meta-01",
                    serverNodeType: NodeType.Metadata,
                    cancellationToken: cts.Token);

                Assert.NotNull(hello);
                Assert.Equal("req-hello-01", hello.RequestId);
                Assert.Equal(1, hello.ProtocolVersion);
                Assert.Equal(NodeType.Client, hello.NodeType);

                // Connection remains open: server reads the next frame on the same stream
                var nextFrame = await FrameCodec.ReadRawFrameAsync(stream, cts.Token);
                Assert.NotNull(nextFrame);
                Assert.Equal(MessageType.Logout, nextFrame.Type);

                var logout = FrameSerialization.DeserializeJson<Logout>(nextFrame.Payload);
                Assert.Equal("req-logout-01", logout.RequestId);

                var logoutResponse = new LogoutResponse(logout.RequestId);
                await FrameCodec.WriteMessageAsync(stream, MessageType.LogoutResponse, logoutResponse, cts.Token);
            }, cts.Token);

            await using var client = await RawTcpTestClient.ConnectAsync("127.0.0.1", port, cts.Token);
            Assert.True(client.IsConnected);

            var helloResponse = await client.HandshakeAsync(
                requestId: "req-hello-01",
                protocolVersion: 1,
                nodeType: NodeType.Client,
                cancellationToken: cts.Token);

            Assert.NotNull(helloResponse);
            Assert.Equal("req-hello-01", helloResponse.RequestId);
            Assert.Equal(1, helloResponse.ProtocolVersion);
            Assert.Equal(NodeType.Metadata, helloResponse.NodeType);
            Assert.Equal("meta-01", helloResponse.NodeId);

            // Verify connection is alive: send subsequent frame on the same connection
            var logoutRequest = new Logout("req-logout-01", "sess-test-token");
            await client.SendMessageAsync(MessageType.Logout, logoutRequest, cts.Token);

            var response = await client.ReceiveMessageAsync<LogoutResponse>(MessageType.LogoutResponse, cts.Token);
            Assert.NotNull(response);
            Assert.Equal("req-logout-01", response.RequestId);

            await serverTask;
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>
    /// Test 2: When protocolVersion != 1, server replies with Error(VersionMismatch)
    /// carrying the matching requestId, and immediately closes the TCP connection.
    /// </summary>
    [Fact]
    public async Task Handshake_VersionMismatch_ReturnsErrorAndClosesConnection()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (listener, port) = StartLoopbackListener();

        try
        {
            var serverTask = Task.Run(async () =>
            {
                using var serverClient = await listener.AcceptTcpClientAsync(cts.Token);
                await using var stream = serverClient.GetStream();

                try
                {
                    await HandshakeHandler.ServerHandshakeAsync(
                        stream,
                        serverNodeId: "meta-01",
                        serverNodeType: NodeType.Metadata,
                        cancellationToken: cts.Token);
                    Assert.Fail("ServerHandshakeAsync should have thrown on version mismatch.");
                }
                catch (DfsException ex)
                {
                    // Server writes ErrorResponse before throwing, then exits using block to close connection
                    Assert.Equal(ErrorCode.VersionMismatch, ex.Code);
                }
            }, cts.Token);

            await using var client = await RawTcpTestClient.ConnectAsync("127.0.0.1", port, cts.Token);

            const string requestId = "req-version-mismatch-99";
            var invalidHello = new Hello(requestId, ProtocolVersion: 99, NodeType: NodeType.Client);
            await client.SendMessageAsync(MessageType.Hello, invalidHello, cts.Token);

            // Server must reply with Error frame
            var responseFrame = await client.ReceiveRawFrameAsync(cts.Token);
            Assert.NotNull(responseFrame);
            Assert.Equal(MessageType.Error, responseFrame.Type);

            var error = FrameSerialization.DeserializeJson<ErrorResponse>(responseFrame.Payload);
            Assert.Equal(ErrorCode.VersionMismatch, error.Code);
            Assert.Equal(requestId, error.RequestId);

            // Server must close the connection: subsequent read returns EOF (null frame / 0 bytes)
            var nextFrame = await client.ReceiveRawFrameAsync(cts.Token);
            Assert.Null(nextFrame);

            var buffer = new byte[1];
            var bytesRead = await client.Stream.ReadAsync(buffer, cts.Token);
            Assert.Equal(0, bytesRead);

            await serverTask;
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>
    /// Test 3: When any message other than Hello is sent first, server replies with
    /// Error(BadRequest) and immediately closes the TCP connection.
    /// </summary>
    [Fact]
    public async Task Handshake_MessageBeforeHello_ReturnsBadRequestAndClosesConnection()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (listener, port) = StartLoopbackListener();

        try
        {
            var serverTask = Task.Run(async () =>
            {
                using var serverClient = await listener.AcceptTcpClientAsync(cts.Token);
                await using var stream = serverClient.GetStream();

                try
                {
                    await HandshakeHandler.ServerHandshakeAsync(
                        stream,
                        serverNodeId: "meta-01",
                        serverNodeType: NodeType.Metadata,
                        cancellationToken: cts.Token);
                    Assert.Fail("ServerHandshakeAsync should have thrown when non-Hello message sent first.");
                }
                catch (DfsException ex)
                {
                    Assert.Equal(ErrorCode.BadRequest, ex.Code);
                }
            }, cts.Token);

            await using var client = await RawTcpTestClient.ConnectAsync("127.0.0.1", port, cts.Token);

            // Send a Login message instead of Hello
            var earlyLogin = new Login("req-early-login", "user", "pass");
            await client.SendMessageAsync(MessageType.Login, earlyLogin, cts.Token);

            var responseFrame = await client.ReceiveRawFrameAsync(cts.Token);
            Assert.NotNull(responseFrame);
            Assert.Equal(MessageType.Error, responseFrame.Type);

            var error = FrameSerialization.DeserializeJson<ErrorResponse>(responseFrame.Payload);
            Assert.Equal(ErrorCode.BadRequest, error.Code);
            Assert.False(string.IsNullOrWhiteSpace(error.RequestId));

            // Server must close connection: EOF
            var nextFrame = await client.ReceiveRawFrameAsync(cts.Token);
            Assert.Null(nextFrame);

            var buffer = new byte[1];
            var bytesRead = await client.Stream.ReadAsync(buffer, cts.Token);
            Assert.Equal(0, bytesRead);

            await serverTask;
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>
    /// Test 4: When a Hello message contains malformed JSON payload, server replies with
    /// Error(BadRequest) and immediately closes the TCP connection.
    /// </summary>
    [Fact]
    public async Task Handshake_MalformedHelloJson_ReturnsBadRequestAndClosesConnection()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (listener, port) = StartLoopbackListener();

        try
        {
            var serverTask = Task.Run(async () =>
            {
                using var serverClient = await listener.AcceptTcpClientAsync(cts.Token);
                await using var stream = serverClient.GetStream();

                try
                {
                    await HandshakeHandler.ServerHandshakeAsync(
                        stream,
                        serverNodeId: "meta-01",
                        serverNodeType: NodeType.Metadata,
                        cancellationToken: cts.Token);
                    Assert.Fail("ServerHandshakeAsync should have thrown on malformed Hello JSON.");
                }
                catch (DfsException ex)
                {
                    Assert.Equal(ErrorCode.BadRequest, ex.Code);
                }
            }, cts.Token);

            await using var client = await RawTcpTestClient.ConnectAsync("127.0.0.1", port, cts.Token);

            // Send Hello frame type but with non-JSON bytes
            var corruptedPayload = Encoding.UTF8.GetBytes("{ not: a: valid: json }");
            await client.SendRawFrameAsync(MessageType.Hello, corruptedPayload, cts.Token);

            var responseFrame = await client.ReceiveRawFrameAsync(cts.Token);
            Assert.NotNull(responseFrame);
            Assert.Equal(MessageType.Error, responseFrame.Type);

            var error = FrameSerialization.DeserializeJson<ErrorResponse>(responseFrame.Payload);
            Assert.Equal(ErrorCode.BadRequest, error.Code);
            Assert.False(string.IsNullOrWhiteSpace(error.RequestId));

            // Verify server closed connection
            var nextFrame = await client.ReceiveRawFrameAsync(cts.Token);
            Assert.Null(nextFrame);

            var buffer = new byte[1];
            var bytesRead = await client.Stream.ReadAsync(buffer, cts.Token);
            Assert.Equal(0, bytesRead);

            await serverTask;
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>
    /// Test 5: Multiple frames sent in sequence (pipelined) on the same TCP connection
    /// are parsed and answered in exact sequential order without framing corruption.
    /// </summary>
    [Fact]
    public async Task Framing_PipelinedFrames_ProcessedInOrderOverTcp()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (listener, port) = StartLoopbackListener();

        try
        {
            const int frameCount = 5;

            var serverTask = Task.Run(async () =>
            {
                using var serverClient = await listener.AcceptTcpClientAsync(cts.Token);
                await using var stream = serverClient.GetStream();

                var hello = await HandshakeHandler.ServerHandshakeAsync(
                    stream,
                    "meta-01",
                    NodeType.Metadata,
                    cts.Token);
                Assert.NotNull(hello);

                for (var i = 0; i < frameCount; i++)
                {
                    var frame = await FrameCodec.ReadRawFrameAsync(stream, cts.Token);
                    Assert.NotNull(frame);
                    Assert.Equal(MessageType.Logout, frame.Type);

                    var msg = FrameSerialization.DeserializeJson<Logout>(frame.Payload);
                    Assert.Equal($"req-pipe-{i}", msg.RequestId);

                    var resp = new LogoutResponse(msg.RequestId);
                    await FrameCodec.WriteMessageAsync(stream, MessageType.LogoutResponse, resp, cts.Token);
                }
            }, cts.Token);

            await using var client = await RawTcpTestClient.ConnectAsync("127.0.0.1", port, cts.Token);

            await client.HandshakeAsync("req-pipe-hello", 1, NodeType.Client, cts.Token);

            // Send all 5 frames back-to-back without waiting for individual replies
            for (var i = 0; i < frameCount; i++)
            {
                var req = new Logout($"req-pipe-{i}", $"sess-{i}");
                await client.SendMessageAsync(MessageType.Logout, req, cts.Token);
            }

            // Receive and verify all 5 responses in FIFO order
            for (var i = 0; i < frameCount; i++)
            {
                var resp = await client.ReceiveMessageAsync<LogoutResponse>(MessageType.LogoutResponse, cts.Token);
                Assert.NotNull(resp);
                Assert.Equal($"req-pipe-{i}", resp.RequestId);
            }

            await serverTask;
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>
    /// Test 6: Frame declaring payload length > 16 MB violates protocol limit.
    /// Receiver rejects with BadRequest and closes the TCP connection.
    /// </summary>
    [Fact]
    public async Task Framing_OversizedFrame_RejectsAndClosesConnection()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (listener, port) = StartLoopbackListener();

        try
        {
            var serverTask = Task.Run(async () =>
            {
                using var serverClient = await listener.AcceptTcpClientAsync(cts.Token);
                await using var stream = serverClient.GetStream();

                try
                {
                    // Server reads incoming frame
                    await FrameCodec.ReadRawFrameAsync(stream, cts.Token);
                    Assert.Fail("Server should have thrown on oversized frame length.");
                }
                catch (DfsException ex)
                {
                    // protocol.md: payload > 16 MB -> BadRequest / close connection
                    Assert.Equal(ErrorCode.BadRequest, ex.Code);
                }
                // Server exits using block, closing the TCP connection
            }, cts.Token);

            await using var client = await RawTcpTestClient.ConnectAsync("127.0.0.1", port, cts.Token);

            // Send 5-byte header claiming payload size 16 MB + 1 byte
            var header = new byte[ProtocolConstants.FrameHeaderLength];
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), ProtocolConstants.MaxPayloadLength + 1);
            header[4] = (byte)MessageType.Stat;

            await client.SendRawBytesAsync(header, cts.Token);

            // Server detects length > 16 MB and terminates the TCP connection
            var nextFrame = await client.ReceiveRawFrameAsync(cts.Token);
            Assert.Null(nextFrame);

            var buffer = new byte[1];
            var bytesRead = await client.Stream.ReadAsync(buffer, cts.Token);
            Assert.Equal(0, bytesRead);

            await serverTask;
        }
        finally
        {
            listener.Stop();
        }
    }
}
