using System.Net;
using System.Net.Sockets;
using Dfs.Shared.Framing;
using Dfs.Shared.Messages;

namespace Dfs.AcceptanceTests;

public class RawTcpTestClientTests
{
    private static (TcpListener Listener, int Port) StartLoopbackListener()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return (listener, port);
    }

    [Fact]
    public async Task Client_ConnectsAndSendsReceivesRawFrame_OverTcp()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (listener, port) = StartLoopbackListener();

        try
        {
            var serverTask = Task.Run(async () =>
            {
                using var serverClient = await listener.AcceptTcpClientAsync(cts.Token);
                await using var serverStream = serverClient.GetStream();

                var received = await FrameCodec.ReadRawFrameAsync(serverStream, cts.Token);
                Assert.NotNull(received);
                Assert.Equal(MessageType.Stat, received.Type);
                Assert.Equal(new byte[] { 1, 2, 3, 4 }, received.Payload);

                await FrameCodec.WriteRawFrameAsync(serverStream, MessageType.StatResponse, new byte[] { 5, 6, 7 }, cts.Token);
            }, cts.Token);

            await using var client = await RawTcpTestClient.ConnectAsync("127.0.0.1", port, cts.Token);
            Assert.True(client.IsConnected);
            Assert.NotNull(client.Stream);

            await client.SendRawFrameAsync(MessageType.Stat, new byte[] { 1, 2, 3, 4 }, cts.Token);
            var response = await client.ReceiveRawFrameAsync(cts.Token);

            Assert.NotNull(response);
            Assert.Equal(MessageType.StatResponse, response.Type);
            Assert.Equal(new byte[] { 5, 6, 7 }, response.Payload);

            await serverTask;
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Client_PerformsHandshake_OverTcp()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (listener, port) = StartLoopbackListener();

        try
        {
            var serverTask = Task.Run(async () =>
            {
                using var serverClient = await listener.AcceptTcpClientAsync(cts.Token);
                await using var serverStream = serverClient.GetStream();

                var hello = await HandshakeHandler.ServerHandshakeAsync(serverStream, "meta-node-1", NodeType.Metadata, cts.Token);
                Assert.NotNull(hello);
                Assert.Equal("req-handshake-1", hello.RequestId);
            }, cts.Token);

            await using var client = await RawTcpTestClient.ConnectAsync($"127.0.0.1:{port}", cts.Token);

            var helloResponse = await client.HandshakeAsync(
                requestId: "req-handshake-1",
                protocolVersion: 1,
                nodeType: NodeType.Client,
                cancellationToken: cts.Token);

            Assert.NotNull(helloResponse);
            Assert.Equal("req-handshake-1", helloResponse.RequestId);
            Assert.Equal(1, helloResponse.ProtocolVersion);
            Assert.Equal(NodeType.Metadata, helloResponse.NodeType);
            Assert.Equal("meta-node-1", helloResponse.NodeId);

            await serverTask;
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Client_SendsAndReceivesTypedMessage_OverTcp()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (listener, port) = StartLoopbackListener();

        try
        {
            var serverTask = Task.Run(async () =>
            {
                using var serverClient = await listener.AcceptTcpClientAsync(cts.Token);
                await using var serverStream = serverClient.GetStream();

                var logout = await FrameCodec.ReadMessageAsync<Logout>(serverStream, MessageType.Logout, cts.Token);
                Assert.NotNull(logout);
                Assert.Equal("req-logout-1", logout.RequestId);
                Assert.Equal("sess-12345", logout.Session);

                var response = new LogoutResponse(logout.RequestId);
                await FrameCodec.WriteMessageAsync(serverStream, MessageType.LogoutResponse, response, cts.Token);
            }, cts.Token);

            await using var client = await RawTcpTestClient.ConnectAsync("127.0.0.1", port, cts.Token);

            var logoutRequest = new Logout("req-logout-1", "sess-12345");
            await client.SendMessageAsync(MessageType.Logout, logoutRequest, cts.Token);

            var response = await client.ReceiveMessageAsync<LogoutResponse>(MessageType.LogoutResponse, cts.Token);
            Assert.NotNull(response);
            Assert.Equal("req-logout-1", response.RequestId);

            await serverTask;
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Client_SendsAndReceivesChunkFrame_OverTcp()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (listener, port) = StartLoopbackListener();

        try
        {
            var chunkData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE };

            var serverTask = Task.Run(async () =>
            {
                using var serverClient = await listener.AcceptTcpClientAsync(cts.Token);
                await using var serverStream = serverClient.GetStream();

                var chunkFrame = await FrameCodec.ReadChunkFrameAsync<WriteChunk>(serverStream, MessageType.WriteChunk, cts.Token);
                Assert.NotNull(chunkFrame);
                Assert.Equal("chunk-001", chunkFrame.Header.ChunkId);
                Assert.Equal(chunkData, chunkFrame.ChunkBytes.ToArray());

                var responseHeader = new WriteChunkResponse(chunkFrame.Header.RequestId, "chunk-001", chunkData.Length, "dummy-sha256");
                await FrameCodec.WriteMessageAsync(serverStream, MessageType.WriteChunkResponse, responseHeader, cts.Token);
            }, cts.Token);

            await using var client = await RawTcpTestClient.ConnectAsync("127.0.0.1", port, cts.Token);

            var writeHeader = new WriteChunk("req-chunk-1", "chunk-001", "token-xyz", chunkData.Length);
            await client.SendChunkFrameAsync(MessageType.WriteChunk, writeHeader, chunkData, cts.Token);

            var writeResponse = await client.ReceiveMessageAsync<WriteChunkResponse>(MessageType.WriteChunkResponse, cts.Token);
            Assert.NotNull(writeResponse);
            Assert.Equal("req-chunk-1", writeResponse.RequestId);
            Assert.Equal("chunk-001", writeResponse.ChunkId);

            await serverTask;
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Client_SendsRawBytes_DirectlyToTcpStream()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (listener, port) = StartLoopbackListener();

        try
        {
            var rawBytes = new byte[] { 0x11, 0x22, 0x33, 0x44 };

            var serverTask = Task.Run(async () =>
            {
                using var serverClient = await listener.AcceptTcpClientAsync(cts.Token);
                await using var serverStream = serverClient.GetStream();

                var buffer = new byte[4];
                await serverStream.ReadExactlyAsync(buffer, cts.Token);
                Assert.Equal(rawBytes, buffer);
            }, cts.Token);

            await using var client = await RawTcpTestClient.ConnectAsync("127.0.0.1", port, cts.Token);
            await client.SendRawBytesAsync(rawBytes, cts.Token);

            await serverTask;
        }
        finally
        {
            listener.Stop();
        }
    }
}
