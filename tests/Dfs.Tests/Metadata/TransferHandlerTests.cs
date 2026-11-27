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
using MessageCreateFolder = Dfs.Shared.Messages.CreateFolder;

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

    private static async Task<CreateUploadResponse> SendCreateUploadAsync(NetworkStream stream, string session, string path, long size, int baseVersion)
    {
        await FrameCodec.WriteMessageAsync(stream, MessageType.CreateUpload, new CreateUpload(Guid.NewGuid().ToString("D"), session, path, size, baseVersion));
        var res = await FrameCodec.ReadMessageAsync<CreateUploadResponse>(stream, MessageType.CreateUploadResponse);
        Assert.NotNull(res);
        return res;
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

    [Fact]
    public async Task CreateUpload_UnauthorizedUser_ReturnsForbidden_EvenWithStaleBaseVersion()
    {
        await _store.SubmitAsync(new RegisterDataServer("ds-1", "127.0.0.1:7001", 100_000_000, 80_000_000, DateTime.UtcNow));

        var host = await StartHostAsync(new RbacAuthorizer(_store));
        var (adminClient, adminStream, adminSession) = await ConnectAndLoginAsync(host);
        using (adminClient)
        await using (adminStream)
        {
            // Admin creates a folder and an existing file
            await FrameCodec.WriteMessageAsync(adminStream, MessageType.CreateFolder, new MessageCreateFolder(Guid.NewGuid().ToString("D"), adminSession, "/private"));
            await FrameCodec.ReadMessageAsync<CreateFolderResponse>(adminStream, MessageType.CreateFolderResponse);

            var createReq = new CreateUpload(Guid.NewGuid().ToString("D"), adminSession, "/private/secret.txt", 100, 0);
            await FrameCodec.WriteMessageAsync(adminStream, MessageType.CreateUpload, createReq);
            var createRes = await FrameCodec.ReadMessageAsync<CreateUploadResponse>(adminStream, MessageType.CreateUploadResponse);
            Assert.NotNull(createRes);

            var sha = Convert.ToHexStringLower(SHA256.HashData(new byte[100]));
            var commitReq = new CommitUploadRequest(Guid.NewGuid().ToString("D"), adminSession, createRes.UploadId, new ValueList<ChecksumInfo>([new ChecksumInfo(createRes.Chunks[0].ChunkId, 100, sha)]));
            await FrameCodec.WriteMessageAsync(adminStream, MessageType.CommitUpload, commitReq);
            await FrameCodec.ReadMessageAsync<CommitUploadResponse>(adminStream, MessageType.CommitUploadResponse);
        }

        // Create non-admin user bob
        var credService = new UserCredentialService(_store);
        await credService.CreateUserAsync("u-bob", "bob", "bob-pass", DateTime.UtcNow);

        var (bobClient, bobStream, bobSession) = await ConnectAndLoginUserAsync(host, "bob", "bob-pass");
        using (bobClient)
        await using (bobStream)
        {
            // 1. Calling CreateUpload on existing file where bob has no write permission:
            // Passing baseVersion = 999 must return Forbidden, NOT Conflict
            var reqExisting = new CreateUpload(Guid.NewGuid().ToString("D"), bobSession, "/private/secret.txt", 100, 999);
            await FrameCodec.WriteMessageAsync(bobStream, MessageType.CreateUpload, reqExisting);
            var ex1 = await Assert.ThrowsAsync<DfsException>(() =>
                FrameCodec.ReadMessageAsync<CreateUploadResponse>(bobStream, MessageType.CreateUploadResponse));
            Assert.Equal(ErrorCode.Forbidden, ex1.Code);

            // 2. Calling CreateUpload on new file in folder where bob has no create permission:
            // Passing baseVersion = 999 must return Forbidden, NOT Conflict
            var reqNew = new CreateUpload(Guid.NewGuid().ToString("D"), bobSession, "/private/new.txt", 100, 999);
            await FrameCodec.WriteMessageAsync(bobStream, MessageType.CreateUpload, reqNew);
            var ex2 = await Assert.ThrowsAsync<DfsException>(() =>
                FrameCodec.ReadMessageAsync<CreateUploadResponse>(bobStream, MessageType.CreateUploadResponse));
            Assert.Equal(ErrorCode.Forbidden, ex2.Code);

            // 3. Parent folder does not exist: must return NotFound
            var reqNonExistent = new CreateUpload(Guid.NewGuid().ToString("D"), bobSession, "/no-such-dir/new.txt", 100, 0);
            await FrameCodec.WriteMessageAsync(bobStream, MessageType.CreateUpload, reqNonExistent);
            var ex3 = await Assert.ThrowsAsync<DfsException>(() =>
                FrameCodec.ReadMessageAsync<CreateUploadResponse>(bobStream, MessageType.CreateUploadResponse));
            Assert.Equal(ErrorCode.NotFound, ex3.Code);
        }
    }

    [Fact]
    public async Task User_CreatesFile_BecomesOwner_CanOpenAndRead()
    {
        await _store.SubmitAsync(new RegisterDataServer("ds-1", "127.0.0.1:7001", 100_000_000, 80_000_000, DateTime.UtcNow));

        var host = await StartHostAsync(new RbacAuthorizer(_store));
        var (adminClient, adminStream, adminSession) = await ConnectAndLoginAsync(host);
        string sharedFolderId;
        using (adminClient)
        await using (adminStream)
        {
            await FrameCodec.WriteMessageAsync(adminStream, MessageType.CreateFolder, new MessageCreateFolder(Guid.NewGuid().ToString("D"), adminSession, "/shared"));
            var folderRes = await FrameCodec.ReadMessageAsync<CreateFolderResponse>(adminStream, MessageType.CreateFolderResponse);
            Assert.NotNull(folderRes);
            sharedFolderId = folderRes.Item.Id;

            // Grant Editor to charlie on /shared
            var credService = new UserCredentialService(_store);
            await credService.CreateUserAsync("u-charlie", "charlie", "charlie-pass", DateTime.UtcNow);
            await _store.SubmitAsync(new SetGrant("g-charlie-shared", BuiltInRoles.EditorId, SubjectType.User, "u-charlie", sharedFolderId, DateTime.UtcNow));
        }

        var (charlieClient, charlieStream, charlieSession) = await ConnectAndLoginUserAsync(host, "charlie", "charlie-pass");
        using (charlieClient)
        await using (charlieStream)
        {
            // Charlie uploads a new file in /shared
            var createReq = new CreateUpload(Guid.NewGuid().ToString("D"), charlieSession, "/shared/charlie.txt", 60, 0);
            await FrameCodec.WriteMessageAsync(charlieStream, MessageType.CreateUpload, createReq);
            var createRes = await FrameCodec.ReadMessageAsync<CreateUploadResponse>(charlieStream, MessageType.CreateUploadResponse);
            Assert.NotNull(createRes);

            var chunkId = createRes.Chunks[0].ChunkId;
            var sha = Convert.ToHexStringLower(SHA256.HashData(new byte[60]));
            var commitReq = new CommitUploadRequest(Guid.NewGuid().ToString("D"), charlieSession, createRes.UploadId, new ValueList<ChecksumInfo>([new ChecksumInfo(chunkId, 60, sha)]));
            await FrameCodec.WriteMessageAsync(charlieStream, MessageType.CommitUpload, commitReq);
            var commitRes = await FrameCodec.ReadMessageAsync<CommitUploadResponse>(charlieStream, MessageType.CommitUploadResponse);
            Assert.NotNull(commitRes);

            // Verify Charlie can now Open the file (because Charlie is Owner)
            var openReq = new Open(Guid.NewGuid().ToString("D"), charlieSession, "/shared/charlie.txt", 0);
            await FrameCodec.WriteMessageAsync(charlieStream, MessageType.Open, openReq);
            var openRes = await FrameCodec.ReadMessageAsync<OpenResponse>(charlieStream, MessageType.OpenResponse);
            Assert.NotNull(openRes);
            Assert.Equal(60, openRes.Size);
            Assert.Single(openRes.Chunks);

            // Verify Charlie was granted Owner role on the file item
            var authorizer = new RbacAuthorizer(_store);
            var shareDecision = await authorizer.AuthorizeAsync("u-charlie", Permission.ItemShare, commitRes.Item.Id);
            Assert.True(shareDecision.Allowed);
        }
    }

    [Fact]
    public async Task Open_EmptyVersion1_ThenNonEmptyVersion2_OpenVersion1ReturnsZeroChunks()
    {
        await _store.SubmitAsync(new RegisterDataServer("ds-1", "127.0.0.1:7001", 100_000_000, 80_000_000, DateTime.UtcNow));

        var host = await StartHostAsync();
        var (client, stream, session) = await ConnectAndLoginAsync(host);
        using (client)
        await using (stream)
        {
            // Upload v1: 0 bytes
            var create1 = new CreateUpload(Guid.NewGuid().ToString("D"), session, "/version-test.txt", 0, 0);
            await FrameCodec.WriteMessageAsync(stream, MessageType.CreateUpload, create1);
            var createRes1 = await FrameCodec.ReadMessageAsync<CreateUploadResponse>(stream, MessageType.CreateUploadResponse);
            Assert.NotNull(createRes1);
            Assert.Empty(createRes1.Chunks);

            var commit1 = new CommitUploadRequest(Guid.NewGuid().ToString("D"), session, createRes1.UploadId, new ValueList<ChecksumInfo>([]));
            await FrameCodec.WriteMessageAsync(stream, MessageType.CommitUpload, commit1);
            var commitRes1 = await FrameCodec.ReadMessageAsync<CommitUploadResponse>(stream, MessageType.CommitUploadResponse);
            Assert.NotNull(commitRes1);
            Assert.Equal(1, commitRes1.Version);

            // Upload v2: 100 bytes (baseVersion 1)
            var create2 = new CreateUpload(Guid.NewGuid().ToString("D"), session, "/version-test.txt", 100, 1);
            await FrameCodec.WriteMessageAsync(stream, MessageType.CreateUpload, create2);
            var createRes2 = await FrameCodec.ReadMessageAsync<CreateUploadResponse>(stream, MessageType.CreateUploadResponse);
            Assert.NotNull(createRes2);

            var sha = Convert.ToHexStringLower(SHA256.HashData(new byte[100]));
            var commit2 = new CommitUploadRequest(Guid.NewGuid().ToString("D"), session, createRes2.UploadId, new ValueList<ChecksumInfo>([new ChecksumInfo(createRes2.Chunks[0].ChunkId, 100, sha)]));
            await FrameCodec.WriteMessageAsync(stream, MessageType.CommitUpload, commit2);
            var commitRes2 = await FrameCodec.ReadMessageAsync<CommitUploadResponse>(stream, MessageType.CommitUploadResponse);
            Assert.NotNull(commitRes2);
            Assert.Equal(2, commitRes2.Version);

            // Open v1 (empty): must succeed and return size 0, chunks empty
            var openV1 = new Open(Guid.NewGuid().ToString("D"), session, "/version-test.txt", 1);
            await FrameCodec.WriteMessageAsync(stream, MessageType.Open, openV1);
            var openRes1 = await FrameCodec.ReadMessageAsync<OpenResponse>(stream, MessageType.OpenResponse);
            Assert.NotNull(openRes1);
            Assert.Equal(1, openRes1.Version);
            Assert.Equal(0, openRes1.Size);
            Assert.Empty(openRes1.Chunks);
        }
    }

    [Fact]
    public async Task AbortUpload_AllowedForCreator_ForbiddenForOtherUser()
    {
        await _store.SubmitAsync(new RegisterDataServer("ds-1", "127.0.0.1:7001", 100_000_000, 80_000_000, DateTime.UtcNow));

        var host = await StartHostAsync(new RbacAuthorizer(_store));
        var (adminClient, adminStream, adminSession) = await ConnectAndLoginAsync(host);
        string folderId;
        using (adminClient)
        await using (adminStream)
        {
            await FrameCodec.WriteMessageAsync(adminStream, MessageType.CreateFolder, new MessageCreateFolder(Guid.NewGuid().ToString("D"), adminSession, "/uploads"));
            var folderRes = await FrameCodec.ReadMessageAsync<CreateFolderResponse>(adminStream, MessageType.CreateFolderResponse);
            Assert.NotNull(folderRes);
            folderId = folderRes.Item.Id;

            var credService = new UserCredentialService(_store);
            await credService.CreateUserAsync("u-user1", "user1", "pass1", DateTime.UtcNow);
            await credService.CreateUserAsync("u-user2", "user2", "pass2", DateTime.UtcNow);

            await _store.SubmitAsync(new SetGrant("g-user1-uploads", BuiltInRoles.EditorId, SubjectType.User, "u-user1", folderId, DateTime.UtcNow));
        }

        var (client1, stream1, session1) = await ConnectAndLoginUserAsync(host, "user1", "pass1");
        string uploadId;
        using (client1)
        await using (stream1)
        {
            var createReq = new CreateUpload(Guid.NewGuid().ToString("D"), session1, "/uploads/user1-file.txt", 100, 0);
            await FrameCodec.WriteMessageAsync(stream1, MessageType.CreateUpload, createReq);
            var createRes = await FrameCodec.ReadMessageAsync<CreateUploadResponse>(stream1, MessageType.CreateUploadResponse);
            Assert.NotNull(createRes);
            uploadId = createRes.UploadId;
        }

        // user2 tries to abort user1's upload -> Forbidden
        var (client2, stream2, session2) = await ConnectAndLoginUserAsync(host, "user2", "pass2");
        using (client2)
        await using (stream2)
        {
            var abortReq2 = new AbortUploadRequest(Guid.NewGuid().ToString("D"), session2, uploadId);
            await FrameCodec.WriteMessageAsync(stream2, MessageType.AbortUpload, abortReq2);
            var ex = await Assert.ThrowsAsync<DfsException>(() =>
                FrameCodec.ReadMessageAsync<AbortUploadResponse>(stream2, MessageType.AbortUploadResponse));
            Assert.Equal(ErrorCode.Forbidden, ex.Code);
        }

        // user1 aborts own upload -> Succeeded
        var (client1b, stream1b, session1b) = await ConnectAndLoginUserAsync(host, "user1", "pass1");
        using (client1b)
        await using (stream1b)
        {
            var abortReq1 = new AbortUploadRequest(Guid.NewGuid().ToString("D"), session1b, uploadId);
            await FrameCodec.WriteMessageAsync(stream1b, MessageType.AbortUpload, abortReq1);
            var abortRes = await FrameCodec.ReadMessageAsync<AbortUploadResponse>(stream1b, MessageType.AbortUploadResponse);
            Assert.NotNull(abortRes);
        }
    }

    [Fact]
    public async Task Open_NegativeVersion_ReturnsBadRequest()
    {
        await _store.SubmitAsync(new RegisterDataServer("ds-1", "127.0.0.1:7001", 100000, 100000, DateTime.UtcNow));
        var host = await StartHostAsync();

        var (client, stream, session) = await ConnectAndLoginAsync(host);
        using var _ = client;
        await using var __ = stream;

        var createRes = await SendCreateUploadAsync(stream, session, "/neg-ver.txt", 4, 0);
        var sha = Convert.ToHexStringLower(SHA256.HashData("test"u8));
        var commitReq = new CommitUploadRequest(Guid.NewGuid().ToString("D"), session, createRes.UploadId,
            new ValueList<ChecksumInfo>([new ChecksumInfo(createRes.Chunks[0].ChunkId, 4, sha)]));
        await FrameCodec.WriteMessageAsync(stream, MessageType.CommitUpload, commitReq);
        await FrameCodec.ReadMessageAsync<CommitUploadResponse>(stream, MessageType.CommitUploadResponse);

        await FrameCodec.WriteMessageAsync(stream, MessageType.Open, new Open(Guid.NewGuid().ToString("D"), session, "/neg-ver.txt", -1));
        var ex = await Assert.ThrowsAsync<DfsException>(() =>
            FrameCodec.ReadMessageAsync<OpenResponse>(stream, MessageType.OpenResponse));
        Assert.Equal(ErrorCode.BadRequest, ex.Code);
    }

    [Fact]
    public async Task Open_UnauthorizedUserOnFolder_ReturnsForbiddenBeforeKindCheck()
    {
        var host = await StartHostAsync(new RbacAuthorizer(_store));

        var credService = new UserCredentialService(_store);
        await credService.CreateUserAsync("u-noaccess", "noaccess", "pass1", DateTime.UtcNow);

        var (adminClient, adminStream, adminSession) = await ConnectAndLoginAsync(host);
        using (adminClient)
        await using (adminStream)
        {
            await FrameCodec.WriteMessageAsync(adminStream, MessageType.CreateFolder,
                new MessageCreateFolder(Guid.NewGuid().ToString("D"), adminSession, "/secret-dir"));
            await FrameCodec.ReadMessageAsync<CreateFolderResponse>(adminStream, MessageType.CreateFolderResponse);
        }

        var (userClient, userStream, userSession) = await ConnectAndLoginUserAsync(host, "noaccess", "pass1");
        using (userClient)
        await using (userStream)
        {
            await FrameCodec.WriteMessageAsync(userStream, MessageType.Open,
                new Open(Guid.NewGuid().ToString("D"), userSession, "/secret-dir", 0));
            var ex = await Assert.ThrowsAsync<DfsException>(() =>
                FrameCodec.ReadMessageAsync<OpenResponse>(userStream, MessageType.OpenResponse));
            Assert.Equal(ErrorCode.Forbidden, ex.Code);
        }
    }

    [Fact]
    public async Task CommitUpload_MissingItemWithNonZeroBaseVersion_ReturnsConflictBeforeItemCreateCheck()
    {
        await _store.SubmitAsync(new RegisterDataServer("ds-1", "127.0.0.1:7001", 100000, 100000, DateTime.UtcNow));
        var host = await StartHostAsync(new RbacAuthorizer(_store));

        var credService = new UserCredentialService(_store);
        await credService.CreateUserAsync("u-writer", "writer", "pass1", DateTime.UtcNow);

        string folderId;
        var (adminClient, adminStream, adminSession) = await ConnectAndLoginAsync(host);
        using (adminClient)
        await using (adminStream)
        {
            await FrameCodec.WriteMessageAsync(adminStream, MessageType.CreateFolder,
                new MessageCreateFolder(Guid.NewGuid().ToString("D"), adminSession, "/work"));
            var folderRes = await FrameCodec.ReadMessageAsync<CreateFolderResponse>(adminStream, MessageType.CreateFolderResponse);
            Assert.NotNull(folderRes);
            folderId = folderRes.Item.Id;
        }

        // Seed an upload record with baseVersion = 1 for an item that no longer exists
        // (writer has no item.create permission on /work, so CommitUpload must return Conflict, not Forbidden).
        var uploadId = "up-missing-item";
        var chunkId = "c-missing-item";
        Assert.True((await _store.SubmitAsync(new Dfs.Shared.Commands.BeginUpload(
            uploadId, "item-missing", folderId, "doc.txt", "u-writer", 2, 1, 4096,
            new ValueList<PlannedChunk>([new PlannedChunk(0, chunkId, new ValueList<string>(["127.0.0.1:7001"]))]),
            DateTime.UtcNow))).Success);

        var (writerClient, writerStream, writerSession) = await ConnectAndLoginUserAsync(host, "writer", "pass1");
        using (writerClient)
        await using (writerStream)
        {
            var shaV2 = Convert.ToHexStringLower(SHA256.HashData("v2"u8));
            await FrameCodec.WriteMessageAsync(writerStream, MessageType.CommitUpload,
                new CommitUploadRequest(Guid.NewGuid().ToString("D"), writerSession, uploadId,
                    new ValueList<ChecksumInfo>([new ChecksumInfo(chunkId, 2, shaV2)])));
            var frame = await FrameCodec.ReadRawFrameAsync(writerStream);
            Assert.NotNull(frame);
            Assert.Equal(MessageType.Error, frame.Type);
            var err = FrameSerialization.DeserializeJson<ErrorResponse>(frame.Payload);
            Assert.Equal(ErrorCode.Conflict, err.Code);
            Assert.NotNull(err.Details);
            Assert.Equal(0, err.Details["currentVersion"].GetInt32());
        }
    }

    private static async Task<(TcpClient Client, NetworkStream Stream, string Session)> ConnectAndLoginUserAsync(MetadataServerHost host, string username, string password)
    {
        var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", host.BoundPort);
        var stream = client.GetStream();

        var hello = new Hello(Guid.NewGuid().ToString("D"), ProtocolConstants.CurrentProtocolVersion, NodeType.Client);
        await HandshakeHandler.ClientHandshakeAsync(stream, hello);

        var login = new Login(Guid.NewGuid().ToString("D"), username, password);
        await FrameCodec.WriteMessageAsync(stream, MessageType.Login, login);
        var loginRes = await FrameCodec.ReadMessageAsync<LoginResponse>(stream, MessageType.LoginResponse);
        Assert.NotNull(loginRes);

        return (client, stream, loginRes.Session);
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
