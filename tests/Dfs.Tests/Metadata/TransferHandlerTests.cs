using System.Net.Sockets;
using System.Security.Cryptography;
using Dfs.MetadataServer.Config;
using Dfs.MetadataServer.Hosting;
using Dfs.MetadataServer.Security;
using Dfs.MetadataServer.Store;
using Dfs.Shared.Commands;
using Dfs.Shared.Framing;
using Dfs.Shared.Messages;
using Dfs.Shared.Metadata;
using Dfs.Shared.Security;
using CommitUploadRequest = Dfs.Shared.Messages.CommitUpload;
using AbortUploadRequest = Dfs.Shared.Messages.AbortUpload;

namespace Dfs.Tests.Metadata;

public sealed class TransferHandlerTests : IAsyncDisposable
{
    private const string TokenKey = "transfer-test-secret-key-1234567890";
    private readonly string _tempDir;
    private readonly MetadataServerConfig _config;
    private readonly MetadataStore _store;
    private readonly DataTokenVerifier _tokenVerifier;
    private MetadataServerHost? _host;

    public TransferHandlerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dfs-transfer-tests-" + Guid.NewGuid().ToString("N"));
        _config = new MetadataServerConfig(
            NodeRole: "metadata",
            NodeId: "m1",
            ListenPort: 0,
            AdvertiseHost: "127.0.0.1",
            MetadataEndpoints: ["127.0.0.1:7000"],
            DataDir: _tempDir,
            TokenKey: TokenKey,
            AdminUser: "admin",
            AdminPassword: "admin-password-123");

        _store = new MetadataStore(_config.OpLogPath, _config.DatabasePath);
        _tokenVerifier = new DataTokenVerifier(TokenKey);
    }

    private async Task<MetadataServerHost> StartHostAsync(IAuthorizer? authorizer = null)
    {
        _host = new MetadataServerHost(_config, _store, authorizer);
        await _host.StartAsync();
        return _host;
    }

    private async Task<(TcpClient Client, NetworkStream Stream, string Session)> ConnectAndLoginAsync(MetadataServerHost host)
    {
        var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", host.BoundPort);
        var stream = client.GetStream();

        var hello = new Hello(Guid.NewGuid().ToString("D"), ProtocolConstants.CurrentProtocolVersion, NodeType.Client);
        await HandshakeHandler.ClientHandshakeAsync(stream, hello);

        var login = new Login(Guid.NewGuid().ToString("D"), _config.AdminUser, _config.AdminPassword);
        await FrameCodec.WriteMessageAsync(stream, MessageType.Login, login);
        var loginRes = await FrameCodec.ReadMessageAsync<LoginResponse>(stream, MessageType.LoginResponse);
        Assert.NotNull(loginRes);

        return (client, stream, loginRes.Session);
    }

    [Fact]
    public async Task CreateUpload_AssignsAliveDataServers_ByHighestFreeSpace()
    {
        // Register two data servers with different capacities
        await _store.SubmitAsync(new RegisterDataServer("ds-1", "127.0.0.1:7001", 100_000_000, 20_000_000, DateTime.UtcNow));
        await _store.SubmitAsync(new RegisterDataServer("ds-2", "127.0.0.1:7002", 100_000_000, 80_000_000, DateTime.UtcNow));

        var host = await StartHostAsync();
        var (client, stream, session) = await ConnectAndLoginAsync(host);
        using (client)
        await using (stream)
        {
            var req = new CreateUpload(Guid.NewGuid().ToString("D"), session, "/bigfile.bin", 10_000_000, 0);
            await FrameCodec.WriteMessageAsync(stream, MessageType.CreateUpload, req);

            var res = await FrameCodec.ReadMessageAsync<CreateUploadResponse>(stream, MessageType.CreateUploadResponse);
            Assert.NotNull(res);
            Assert.Equal(req.RequestId, res.RequestId);
            Assert.NotEmpty(res.UploadId);
            Assert.Equal(3, res.Chunks.Count); // ceil(10MB / 4MB) = 3 chunks

            // Chunks should be allocated to ds-2 (which had 80MB free space)
            Assert.Equal("127.0.0.1:7002", res.Chunks[0].Servers[0]);

            // Tokens must be valid write tokens (throws if invalid)
            _tokenVerifier.Verify(res.Chunks[0].Token, res.Chunks[0].ChunkId, "w");
        }
    }

    [Fact]
    public async Task CreateUpload_FailsUnavailable_WhenNoAliveDataServers()
    {
        var host = await StartHostAsync();
        var (client, stream, session) = await ConnectAndLoginAsync(host);
        using (client)
        await using (stream)
        {
            var req = new CreateUpload(Guid.NewGuid().ToString("D"), session, "/file.txt", 100, 0);
            await FrameCodec.WriteMessageAsync(stream, MessageType.CreateUpload, req);

            var ex = await Assert.ThrowsAsync<DfsException>(() =>
                FrameCodec.ReadMessageAsync<CreateUploadResponse>(stream, MessageType.CreateUploadResponse));
            Assert.Equal(ErrorCode.Unavailable, ex.Code);
        }
    }

    [Fact]
    public async Task CreateUpload_FailsConflict_WhenStaleBaseVersion()
    {
        await _store.SubmitAsync(new RegisterDataServer("ds-1", "127.0.0.1:7001", 100_000_000, 80_000_000, DateTime.UtcNow));

        var host = await StartHostAsync();
        var (client, stream, session) = await ConnectAndLoginAsync(host);
        using (client)
        await using (stream)
        {
            // First upload
            var req1 = new CreateUpload(Guid.NewGuid().ToString("D"), session, "/file.txt", 100, 0);
            await FrameCodec.WriteMessageAsync(stream, MessageType.CreateUpload, req1);
            var res1 = await FrameCodec.ReadMessageAsync<CreateUploadResponse>(stream, MessageType.CreateUploadResponse);
            Assert.NotNull(res1);

            var sha = Convert.ToHexStringLower(SHA256.HashData(new byte[100]));
            var commit1 = new CommitUploadRequest(Guid.NewGuid().ToString("D"), session, res1.UploadId, new ValueList<ChecksumInfo>([new ChecksumInfo(res1.Chunks[0].ChunkId, 100, sha)]));
            await FrameCodec.WriteMessageAsync(stream, MessageType.CommitUpload, commit1);
            var commitRes1 = await FrameCodec.ReadMessageAsync<CommitUploadResponse>(stream, MessageType.CommitUploadResponse);
            Assert.NotNull(commitRes1);
            Assert.Equal(1, commitRes1.Version);

            // Stale baseVersion = 0 when version is now 1
            var reqStale = new CreateUpload(Guid.NewGuid().ToString("D"), session, "/file.txt", 120, 0);
            await FrameCodec.WriteMessageAsync(stream, MessageType.CreateUpload, reqStale);

            var ex = await Assert.ThrowsAsync<DfsException>(() =>
                FrameCodec.ReadMessageAsync<CreateUploadResponse>(stream, MessageType.CreateUploadResponse));
            Assert.Equal(ErrorCode.Conflict, ex.Code);
        }
    }

    [Fact]
    public async Task Open_ReturnsReadTokens_AndLocations()
    {
        await _store.SubmitAsync(new RegisterDataServer("ds-1", "127.0.0.1:7001", 100_000_000, 80_000_000, DateTime.UtcNow));

        var host = await StartHostAsync();
        var (client, stream, session) = await ConnectAndLoginAsync(host);
        using (client)
        await using (stream)
        {
            // Upload file
            var createReq = new CreateUpload(Guid.NewGuid().ToString("D"), session, "/open-test.txt", 50, 0);
            await FrameCodec.WriteMessageAsync(stream, MessageType.CreateUpload, createReq);
            var createRes = await FrameCodec.ReadMessageAsync<CreateUploadResponse>(stream, MessageType.CreateUploadResponse);
            Assert.NotNull(createRes);

            var chunkId = createRes.Chunks[0].ChunkId;
            var sha = Convert.ToHexStringLower(SHA256.HashData(new byte[50]));
            var commitReq = new CommitUploadRequest(Guid.NewGuid().ToString("D"), session, createRes.UploadId, new ValueList<ChecksumInfo>([new ChecksumInfo(chunkId, 50, sha)]));
            await FrameCodec.WriteMessageAsync(stream, MessageType.CommitUpload, commitReq);
            await FrameCodec.ReadMessageAsync<CommitUploadResponse>(stream, MessageType.CommitUploadResponse);

            // Open file
            var openReq = new Open(Guid.NewGuid().ToString("D"), session, "/open-test.txt", 0);
            await FrameCodec.WriteMessageAsync(stream, MessageType.Open, openReq);
            var openRes = await FrameCodec.ReadMessageAsync<OpenResponse>(stream, MessageType.OpenResponse);

            Assert.NotNull(openRes);
            Assert.Equal(1, openRes.Version);
            Assert.Equal(50, openRes.Size);
            Assert.Single(openRes.Chunks);
            Assert.Equal(chunkId, openRes.Chunks[0].ChunkId);
            Assert.Equal(sha, openRes.Chunks[0].Sha256);
            Assert.Contains("127.0.0.1:7001", openRes.Chunks[0].Servers);

            // Token must be a valid read token
            _tokenVerifier.Verify(openRes.Chunks[0].Token, chunkId, "r");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }
        _store.Dispose();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
        }
    }
}
