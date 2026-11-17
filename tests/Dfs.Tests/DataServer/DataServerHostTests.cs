using System.Net.Sockets;
using System.Security.Cryptography;
using Dfs.DataServer.Config;
using Dfs.DataServer.Hosting;
using Dfs.DataServer.Storage;
using Dfs.MetadataServer.Security;
using Dfs.Shared.Framing;
using Dfs.Shared.Messages;

namespace Dfs.Tests.DataServer;

public sealed class DataServerHostTests : IAsyncDisposable
{
    private const string TokenKey = "data-server-test-hmac-key-shared-123456";
    private readonly string _tempDir;
    private readonly ChunkStorage _storage;
    private readonly DataServerConfig _config;
    private readonly TestTimeProvider _time;
    private readonly DataTokenSigner _signer;
    private DataServerHost? _host;

    public DataServerHostTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dfs-data-host-tests-" + Guid.NewGuid().ToString("N"));
        _storage = new ChunkStorage(_tempDir, configuredCapacity: 10L * 1024 * 1024 * 1024);
        _config = new DataServerConfig(
            NodeRole: "data",
            NodeId: "ds-test-1",
            ListenPort: 0,
            AdvertiseHost: "127.0.0.1",
            MetadataEndpoints: ["127.0.0.1:7000"],
            DataDir: _tempDir,
            TokenKey: TokenKey,
            CapacityBytes: 10L * 1024 * 1024 * 1024);

        _time = new TestTimeProvider(new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero));
        _signer = new DataTokenSigner(TokenKey, _time);
    }

    private async Task<DataServerHost> StartHostAsync()
    {
        _host = new DataServerHost(_config, _storage, _time);
        await _host.StartAsync();
        return _host;
    }

    private async Task<(TcpClient Client, NetworkStream Stream)> ConnectAndHandshakeAsync(DataServerHost host)
    {
        var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", host.BoundPort);
        var stream = client.GetStream();

        var hello = new Hello(
            Guid.NewGuid().ToString("D"),
            ProtocolConstants.CurrentProtocolVersion,
            NodeType.Client);

        var response = await HandshakeHandler.ClientHandshakeAsync(stream, hello);
        Assert.Equal(ProtocolConstants.CurrentProtocolVersion, response.ProtocolVersion);
        Assert.Equal(NodeType.Data, response.NodeType);
        Assert.Equal(_config.NodeId, response.NodeId);

        return (client, stream);
    }

    [Fact]
    public async Task Host_AcceptsHelloHandshake_WithNodeTypeData()
    {
        var host = await StartHostAsync();
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", host.BoundPort);
        await using var stream = client.GetStream();

        var hello = new Hello(
            Guid.NewGuid().ToString("D"),
            ProtocolConstants.CurrentProtocolVersion,
            NodeType.Client);

        var response = await HandshakeHandler.ClientHandshakeAsync(stream, hello);
        Assert.Equal(NodeType.Data, response.NodeType);
        Assert.Equal("ds-test-1", response.NodeId);
    }

    [Fact]
    public async Task WriteChunk_WithValidToken_Succeeds_AndStoresChunk()
    {
        var host = await StartHostAsync();
        var (client, stream) = await ConnectAndHandshakeAsync(host);
        using (client)
        await using (stream)
        {
            var chunkId = "c-01HWRITE001";
            var token = _signer.Sign("user-1", chunkId, "w");
            var data = "Chunk data content bytes"u8.ToArray();
            var expectedSha = Convert.ToHexStringLower(SHA256.HashData(data));

            var header = new WriteChunk(Guid.NewGuid().ToString("D"), chunkId, token, data.Length);
            await FrameCodec.WriteChunkFrameAsync(stream, MessageType.WriteChunk, header, data);

            var response = await FrameCodec.ReadMessageAsync<WriteChunkResponse>(stream, MessageType.WriteChunkResponse);
            Assert.NotNull(response);
            Assert.Equal(header.RequestId, response.RequestId);
            Assert.Equal(chunkId, response.ChunkId);
            Assert.Equal(data.Length, response.Size);
            Assert.Equal(expectedSha, response.Sha256);

            var readFromStorage = await _storage.ReadChunkAsync(chunkId);
            Assert.Equal(data, readFromStorage.Data);
        }
    }

    [Fact]
    public async Task WriteChunk_WithInvalidToken_FailsWithTokenInvalid()
    {
        var host = await StartHostAsync();
        var (client, stream) = await ConnectAndHandshakeAsync(host);
        using (client)
        await using (stream)
        {
            var chunkId = "c-01HBADTOKEN";
            var invalidToken = "invalid.token.payload";
            var data = new byte[] { 1, 2, 3 };

            var header = new WriteChunk(Guid.NewGuid().ToString("D"), chunkId, invalidToken, data.Length);
            await FrameCodec.WriteChunkFrameAsync(stream, MessageType.WriteChunk, header, data);

            var ex = await Assert.ThrowsAsync<DfsException>(() =>
                FrameCodec.ReadMessageAsync<WriteChunkResponse>(stream, MessageType.WriteChunkResponse));

            Assert.Equal(ErrorCode.TokenInvalid, ex.Code);
        }
    }

    [Fact]
    public async Task WriteChunk_WithExpiredToken_FailsWithTokenExpired()
    {
        var host = await StartHostAsync();
        var (client, stream) = await ConnectAndHandshakeAsync(host);
        using (client)
        await using (stream)
        {
            var chunkId = "c-01HEXPIRED";
            var token = _signer.Sign("user-1", chunkId, "w");

            // Advance time past 300s expiry + 120s clock skew
            _time.Advance(TimeSpan.FromSeconds(450));

            var data = new byte[] { 10, 20, 30 };
            var header = new WriteChunk(Guid.NewGuid().ToString("D"), chunkId, token, data.Length);
            await FrameCodec.WriteChunkFrameAsync(stream, MessageType.WriteChunk, header, data);

            var ex = await Assert.ThrowsAsync<DfsException>(() =>
                FrameCodec.ReadMessageAsync<WriteChunkResponse>(stream, MessageType.WriteChunkResponse));

            Assert.Equal(ErrorCode.TokenExpired, ex.Code);
        }
    }

    [Fact]
    public async Task WriteChunk_WithSizeMismatch_FailsWithBadRequest()
    {
        var host = await StartHostAsync();
        var (client, stream) = await ConnectAndHandshakeAsync(host);
        using (client)
        await using (stream)
        {
            var chunkId = "c-01HMISMATCH";
            var token = _signer.Sign("user-1", chunkId, "w");
            var data = new byte[] { 1, 2, 3, 4, 5 };

            // Declared size 100 does not match 5 bytes
            var header = new WriteChunk(Guid.NewGuid().ToString("D"), chunkId, token, 100);
            await FrameCodec.WriteChunkFrameAsync(stream, MessageType.WriteChunk, header, data);

            var ex = await Assert.ThrowsAsync<DfsException>(() =>
                FrameCodec.ReadMessageAsync<WriteChunkResponse>(stream, MessageType.WriteChunkResponse));

            Assert.Equal(ErrorCode.BadRequest, ex.Code);
        }
    }

    [Fact]
    public async Task ReadChunk_WithValidToken_ReturnsChunkFrameAndPayload()
    {
        var host = await StartHostAsync();

        var chunkId = "c-01HREADVALID";
        var data = "Stored chunk content for testing"u8.ToArray();
        await _storage.WriteChunkAsync(chunkId, data);

        var (client, stream) = await ConnectAndHandshakeAsync(host);
        using (client)
        await using (stream)
        {
            var token = _signer.Sign("user-1", chunkId, "r");
            var request = new ReadChunk(Guid.NewGuid().ToString("D"), chunkId, token);
            await FrameCodec.WriteMessageAsync(stream, MessageType.ReadChunk, request);

            var frame = await FrameCodec.ReadChunkFrameAsync<ReadChunkResponse>(stream, MessageType.ReadChunkResponse);
            Assert.NotNull(frame);
            Assert.Equal(request.RequestId, frame.Header.RequestId);
            Assert.Equal(chunkId, frame.Header.ChunkId);
            Assert.Equal(data.Length, frame.Header.Size);
            Assert.Equal(data, frame.ChunkBytes.ToArray());
        }
    }

    [Fact]
    public async Task ReadChunk_WithWrongOperationToken_FailsWithTokenInvalid()
    {
        var host = await StartHostAsync();

        var chunkId = "c-01HWRONGOP";
        var data = new byte[] { 7, 8, 9 };
        await _storage.WriteChunkAsync(chunkId, data);

        var (client, stream) = await ConnectAndHandshakeAsync(host);
        using (client)
        await using (stream)
        {
            // Write token supplied for read operation
            var writeToken = _signer.Sign("user-1", chunkId, "w");
            var request = new ReadChunk(Guid.NewGuid().ToString("D"), chunkId, writeToken);
            await FrameCodec.WriteMessageAsync(stream, MessageType.ReadChunk, request);

            var ex = await Assert.ThrowsAsync<DfsException>(() =>
                FrameCodec.ReadChunkFrameAsync<ReadChunkResponse>(stream, MessageType.ReadChunkResponse));

            Assert.Equal(ErrorCode.TokenInvalid, ex.Code);
        }
    }

    [Fact]
    public async Task ReadChunk_NonExistentChunk_ReturnsNotFound()
    {
        var host = await StartHostAsync();
        var (client, stream) = await ConnectAndHandshakeAsync(host);
        using (client)
        await using (stream)
        {
            var chunkId = "c-01HNOTFOUND";
            var token = _signer.Sign("user-1", chunkId, "r");
            var request = new ReadChunk(Guid.NewGuid().ToString("D"), chunkId, token);
            await FrameCodec.WriteMessageAsync(stream, MessageType.ReadChunk, request);

            var ex = await Assert.ThrowsAsync<DfsException>(() =>
                FrameCodec.ReadChunkFrameAsync<ReadChunkResponse>(stream, MessageType.ReadChunkResponse));

            Assert.Equal(ErrorCode.NotFound, ex.Code);
        }
    }

    [Fact]
    public async Task DeleteChunk_DeletesChunks_AndReturnsCount()
    {
        var host = await StartHostAsync();
        var chunk1 = "c-01HDEL1";
        var chunk2 = "c-01HDEL2";
        await _storage.WriteChunkAsync(chunk1, new byte[] { 1 });
        await _storage.WriteChunkAsync(chunk2, new byte[] { 2 });

        var (client, stream) = await ConnectAndHandshakeAsync(host);
        using (client)
        await using (stream)
        {
            var request = new DeleteChunk(Guid.NewGuid().ToString("D"), [chunk1, chunk2, "c-missing"]);
            await FrameCodec.WriteMessageAsync(stream, MessageType.DeleteChunk, request);

            var response = await FrameCodec.ReadMessageAsync<DeleteChunkResponse>(stream, MessageType.DeleteChunkResponse);
            Assert.NotNull(response);
            Assert.Equal(request.RequestId, response.RequestId);
            Assert.Equal(2, response.Deleted);
        }
    }

    [Fact]
    public async Task UnsupportedMessage_ReturnsBadRequest()
    {
        var host = await StartHostAsync();
        var (client, stream) = await ConnectAndHandshakeAsync(host);
        using (client)
        await using (stream)
        {
            var request = new Login(Guid.NewGuid().ToString("D"), "admin", "password");
            await FrameCodec.WriteMessageAsync(stream, MessageType.Login, request);

            var ex = await Assert.ThrowsAsync<DfsException>(() =>
                FrameCodec.ReadMessageAsync<LoginResponse>(stream, MessageType.LoginResponse));

            Assert.Equal(ErrorCode.BadRequest, ex.Code);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }
        _storage.Dispose();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
        }
    }

    private sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now += delta;
    }
}
