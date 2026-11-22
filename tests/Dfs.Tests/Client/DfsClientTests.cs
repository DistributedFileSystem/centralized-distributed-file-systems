using System.Net;
using System.Net.Sockets;
using Dfs.Client.Core;
using Dfs.Fakes;
using Dfs.Shared.Framing;
using Dfs.Shared.Messages;

namespace Dfs.Tests.Client;

public class DfsClientTests : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _disposables = new();

    public async ValueTask DisposeAsync()
    {
        foreach (var d in _disposables)
        {
            try
            {
                await d.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    private T Register<T>(T disposable) where T : IAsyncDisposable
    {
        _disposables.Add(disposable);
        return disposable;
    }

    private (FakeDataServer Data, FakeMetadataServer Meta) StartFakeCluster()
    {
        var data = Register(new FakeDataServer());
        data.Start();
        var meta = Register(new FakeMetadataServer(data));
        meta.Start();
        return (data, meta);
    }

    // -------------------------------------------------------------
    // Options & Parsing Tests
    // -------------------------------------------------------------

    [Fact]
    public void Options_ParseEndpoints_HandlesVariousDelimitersAndWhitespace()
    {
        var result = DfsClientOptions.ParseEndpoints(" 127.0.0.1:7000, 10.0.0.1:7001 ; 192.168.1.1:7002 , ");
        Assert.Equal(new[] { "127.0.0.1:7000", "10.0.0.1:7001", "192.168.1.1:7002" }, result);

        Assert.Empty(DfsClientOptions.ParseEndpoints(null));
        Assert.Empty(DfsClientOptions.ParseEndpoints("   "));
    }

    // -------------------------------------------------------------
    // Connection & Handshake Tests
    // -------------------------------------------------------------

    [Fact]
    public async Task ConnectAsync_ConnectsToFakeMetadataServer_AndPerformsHandshake()
    {
        var (_, server) = StartFakeCluster();

        var client = Register(new DfsClient(server.Address));
        Assert.False(client.IsConnected);

        await client.ConnectAsync();
        Assert.True(client.IsConnected);
        Assert.Equal(server.Address, client.ConnectedEndpoint);
        Assert.False(string.IsNullOrEmpty(client.ConnectedNodeId));

        // Subsequent call is idempotent
        await client.ConnectAsync();
        Assert.True(client.IsConnected);
    }

    [Fact]
    public async Task ConnectAsync_FailsOverToNextEndpoint_WhenFirstFails()
    {
        var (_, server) = StartFakeCluster();

        // 127.0.0.1:1 is guaranteed not running in our test
        var client = Register(new DfsClient(new[] { "127.0.0.1:1", server.Address }));
        await client.ConnectAsync();

        Assert.True(client.IsConnected);
        Assert.Equal(server.Address, client.ConnectedEndpoint);
    }

    [Fact]
    public async Task ConnectAsync_ThrowsUnavailable_WhenAllEndpointsDown()
    {
        var client = Register(new DfsClient(new[] { "127.0.0.1:1", "127.0.0.1:2" }));
        var ex = await Assert.ThrowsAsync<DfsException>(() => client.ConnectAsync());
        Assert.Equal(ErrorCode.Unavailable, ex.Code);
    }

    // -------------------------------------------------------------
    // Login & Session Tests
    // -------------------------------------------------------------

    [Fact]
    public async Task Operations_RequireLogin_WhenNotLoggedIn()
    {
        var (_, server) = StartFakeCluster();

        var client = Register(new DfsClient(server.Address));
        Assert.False(client.IsLoggedIn);

        var ex = await Assert.ThrowsAsync<DfsException>(() => client.ListDirAsync("/"));
        Assert.Equal(ErrorCode.Unauthorized, ex.Code);
    }

    [Fact]
    public async Task LoginAsync_RejectsEmptyUsername()
    {
        var client = Register(new DfsClient("127.0.0.1:7000"));
        var ex = await Assert.ThrowsAsync<DfsException>(() => client.LoginAsync("", "pwd"));
        Assert.Equal(ErrorCode.BadRequest, ex.Code);
    }

    [Fact]
    public async Task LoginAsync_SucceedsWithValidCredentials_AndSetsSessionState()
    {
        var (_, server) = StartFakeCluster();

        var client = Register(new DfsClient(server.Address));
        var res = await client.LoginAsync("demo", "demo");

        Assert.True(client.IsLoggedIn);
        Assert.Equal(res.Session, client.Session);
        Assert.False(string.IsNullOrEmpty(client.CurrentUser?.Id));
        Assert.Equal("demo", client.CurrentUser?.Username);
        Assert.NotNull(client.SystemRoles);
        Assert.NotNull(client.Permissions);
    }

    [Fact]
    public async Task LoginAsync_ThrowsUnauthorized_OnBadCredentials()
    {
        var (_, server) = StartFakeCluster();

        var client = Register(new DfsClient(server.Address));
        var ex = await Assert.ThrowsAsync<DfsException>(() => client.LoginAsync("wrong", "pwd"));

        Assert.Equal(ErrorCode.Unauthorized, ex.Code);
        Assert.False(client.IsLoggedIn);
        Assert.Null(client.Session);
    }

    [Fact]
    public async Task LogoutAsync_ClearsSessionState()
    {
        var (_, server) = StartFakeCluster();

        var client = Register(new DfsClient(server.Address));
        await client.LoginAsync("demo", "demo");
        Assert.True(client.IsLoggedIn);

        await client.LogoutAsync();
        Assert.False(client.IsLoggedIn);
        Assert.Null(client.Session);

        // Further requests throw Unauthorized
        var ex = await Assert.ThrowsAsync<DfsException>(() => client.ListDirAsync("/"));
        Assert.Equal(ErrorCode.Unauthorized, ex.Code);
    }

    // -------------------------------------------------------------
    // Namespace Query Operations (Against FakeMetadataServer)
    // -------------------------------------------------------------

    [Fact]
    public async Task ListDirAsync_And_StatAsync_ReturnExpectedData()
    {
        var (_, server) = StartFakeCluster();

        var client = Register(new DfsClient(server.Address));
        await client.LoginAsync("demo", "demo");

        var children = await client.ListDirAsync("/projects/dfs");
        Assert.Contains(children, c => c.Name == "readme.txt");

        var item = await client.StatAsync("/projects/dfs/readme.txt");
        Assert.Equal("readme.txt", item.Name);
        Assert.Equal(ItemKind.File, item.Kind);
    }

    [Fact]
    public async Task ListVersionsAsync_ReturnsVersions()
    {
        var (_, server) = StartFakeCluster();

        var client = Register(new DfsClient(server.Address));
        await client.LoginAsync("demo", "demo");

        var versions = await client.ListVersionsAsync("/projects/dfs/readme.txt");
        Assert.NotEmpty(versions);
    }

    [Fact]
    public async Task ClusterStatusAsync_ReturnsRegisteredDataServers()
    {
        var (_, server) = StartFakeCluster();

        var client = Register(new DfsClient(server.Address));
        await client.LoginAsync("demo", "demo");

        var servers = await client.ClusterStatusAsync();
        Assert.Single(servers);
        Assert.Equal("d-demo", servers[0].NodeId);
    }

    // -------------------------------------------------------------
    // Namespace Mutation Operations (Against MockMetadataServer)
    // -------------------------------------------------------------

    [Fact]
    public async Task NamespaceMutations_DispatchCorrectMessages_AndParseResponses()
    {
        await using var mock = new MockMetadataServer();
        mock.Start();

        var client = Register(new DfsClient(mock.Address));
        await client.LoginAsync("alice", "secret");

        // CreateFolder
        var created = await client.CreateFolderAsync("/projects/new");
        Assert.Equal("/projects/new", created.Path);
        Assert.Equal("new", created.Name);
        Assert.Equal(ItemKind.Folder, created.Kind);

        // Rename
        var renamed = await client.RenameAsync("/projects/new", "renamed");
        Assert.Equal("/projects/renamed", renamed.Path);
        Assert.Equal("renamed", renamed.Name);

        // Move
        var moved = await client.MoveAsync("/projects/renamed", "/archive");
        Assert.Equal("/archive/renamed", moved.Path);

        // DeleteItem
        await client.DeleteItemAsync("/archive/renamed");
    }

    [Fact]
    public async Task ServerError_ThrowsDfsException_WithCorrectErrorCode()
    {
        await using var mock = new MockMetadataServer();
        mock.Start();

        var client = Register(new DfsClient(mock.Address));
        await client.LoginAsync("alice", "secret");

        mock.RejectWith = ErrorCode.AlreadyExists;
        var ex = await Assert.ThrowsAsync<DfsException>(() => client.CreateFolderAsync("/duplicate"));
        Assert.Equal(ErrorCode.AlreadyExists, ex.Code);
    }

    [Fact]
    public async Task ServerUnauthorized_ClearsSessionLocally()
    {
        await using var mock = new MockMetadataServer();
        mock.Start();

        var client = Register(new DfsClient(mock.Address));
        await client.LoginAsync("alice", "secret");
        Assert.True(client.IsLoggedIn);

        mock.RejectWith = ErrorCode.Unauthorized;
        var ex = await Assert.ThrowsAsync<DfsException>(() => client.StatAsync("/any"));
        Assert.Equal(ErrorCode.Unauthorized, ex.Code);
        Assert.False(client.IsLoggedIn);
        Assert.Null(client.Session);
    }

    [Fact]
    public async Task UnimplementedOperations_ThrowNotSupportedException()
    {
        var client = Register(new DfsClient("127.0.0.1:7000"));

        await Assert.ThrowsAsync<NotSupportedException>(() => client.CreateUserAsync("u", "p"));
        await Assert.ThrowsAsync<NotSupportedException>(() => client.ListUsersAsync());
        await Assert.ThrowsAsync<NotSupportedException>(() => client.SetUserDisabledAsync("u", true));
        await Assert.ThrowsAsync<NotSupportedException>(() => client.ResetPasswordAsync("u", "p"));
        await Assert.ThrowsAsync<NotSupportedException>(() => client.CreateGroupAsync("g"));
        await Assert.ThrowsAsync<NotSupportedException>(() => client.ListGroupsAsync());
        await Assert.ThrowsAsync<NotSupportedException>(() => client.SetGroupMemberAsync("g", "u", true));
        await Assert.ThrowsAsync<NotSupportedException>(() => client.CreateRoleAsync("r", RoleScope.System, new[] { "p" }));
        await Assert.ThrowsAsync<NotSupportedException>(() => client.ListRolesAsync());
        await Assert.ThrowsAsync<NotSupportedException>(() => client.UpdateRoleAsync("r", new[] { "p" }));
        await Assert.ThrowsAsync<NotSupportedException>(() => client.CreateGrantAsync("r", SubjectType.User, "u"));
        await Assert.ThrowsAsync<NotSupportedException>(() => client.ListGrantsAsync());
        await Assert.ThrowsAsync<NotSupportedException>(() => client.RevokeGrantAsync("g"));
    }

    [Fact]
    public void ParseEndpoint_InvalidFormats_ThrowArgumentException()
    {
        Assert.Throws<ArgumentException>(() => DfsClient.ParseEndpoint(""));
        Assert.Throws<ArgumentException>(() => DfsClient.ParseEndpoint("   "));
        Assert.Throws<ArgumentException>(() => DfsClient.ParseEndpoint(":7000"));
        Assert.Throws<ArgumentException>(() => DfsClient.ParseEndpoint("[]:7000"));
        Assert.Throws<ArgumentException>(() => DfsClient.ParseEndpoint("host:0"));
        Assert.Throws<ArgumentException>(() => DfsClient.ParseEndpoint("host:99999"));
        Assert.Throws<ArgumentException>(() => DfsClient.ParseEndpoint("host:-1"));
        Assert.Throws<ArgumentException>(() => DfsClient.ParseEndpoint("host"));
        Assert.Throws<ArgumentException>(() => DfsClient.ParseEndpoint("[::1]"));

        var (host, port) = DfsClient.ParseEndpoint("127.0.0.1:7000");
        Assert.Equal("127.0.0.1", host);
        Assert.Equal(7000, port);

        var (v6Host, v6Port) = DfsClient.ParseEndpoint("[::1]:8080");
        Assert.Equal("::1", v6Host);
        Assert.Equal(8080, v6Port);
    }

    [Fact]
    public async Task Failover_WithMalformedAndDeadEndpoints_StillReachesValidEndpoint()
    {
        var (_, server) = StartFakeCluster();

        // Endpoints list contains malformed, unreachable port, and then valid server
        var client = Register(new DfsClient(new[] { ":7000", "127.0.0.1:65534", server.Address }));
        await client.ConnectAsync();
        Assert.True(client.IsConnected);
        Assert.Equal(server.Address, client.ConnectedEndpoint);
    }

    [Fact]
    public async Task CallerCancellation_PropagatesOperationCanceledException()
    {
        var (_, server) = StartFakeCluster();
        var client = Register(new DfsClient(server.Address));

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Already cancelled

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ConnectAsync(cts.Token));
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        var client = new DfsClient("127.0.0.1:7000");
        await client.DisposeAsync();
        await client.DisposeAsync(); // Second call must not throw ObjectDisposedException on semaphore
        client.Dispose();
    }

    [Fact]
    public async Task Hello_ReturningVersionMismatch_ThrowsVersionMismatchNotUnavailable()
    {
        await using var mock = new MockMetadataServer { RejectHelloWithVersionMismatch = true };
        mock.Start();

        var client = Register(new DfsClient(mock.Address));
        var ex = await Assert.ThrowsAsync<DfsException>(() => client.ConnectAsync());
        Assert.Equal(ErrorCode.VersionMismatch, ex.Code);
    }

    [Fact]
    public async Task LoginAsync_Failure_DoesNotClearExistingSession()
    {
        await using var mock = new MockMetadataServer();
        mock.Start();

        var client = Register(new DfsClient(mock.Address));
        var res = await client.LoginAsync("alice", "secret");
        Assert.True(client.IsLoggedIn);
        Assert.Equal("mock-session-123", client.Session);
        Assert.Equal("alice", client.CurrentUser?.Username);

        // Subsequent failed login attempt with wrong credentials
        mock.RejectWith = ErrorCode.Unauthorized;
        var ex = await Assert.ThrowsAsync<DfsException>(() => client.LoginAsync("bob", "wrong-pwd"));
        Assert.Equal(ErrorCode.Unauthorized, ex.Code);

        // Previous session should still be intact and valid
        Assert.True(client.IsLoggedIn);
        Assert.Equal(res.Session, client.Session);
        Assert.Equal("alice", client.CurrentUser?.Username);
    }

    [Fact]
    public async Task LocalSessionExpired_ThrowsUnauthorized()
    {
        await using var mock = new MockMetadataServer
        {
            SessionDuration = TimeSpan.FromMilliseconds(-100) // already expired
        };
        mock.Start();

        var client = Register(new DfsClient(mock.Address));
        await client.LoginAsync("alice", "secret");

        // Expired session should be detected locally without sending request
        Assert.False(client.IsLoggedIn);
        Assert.Null(client.Session);

        var ex = await Assert.ThrowsAsync<DfsException>(() => client.CreateFolderAsync("/test"));
        Assert.Equal(ErrorCode.Unauthorized, ex.Code);
        Assert.Contains("expired", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TcpDrop_ReconnectionPreservesSession()
    {
        await using var mock = new MockMetadataServer();
        mock.Start();

        var client = Register(new DfsClient(mock.Address));
        await client.LoginAsync("alice", "secret");
        Assert.True(client.IsLoggedIn);
        var originalSession = client.Session;

        // Disconnect underlying connection abruptly (simulating server drop or network glitch)
        mock.CloseAllClients();

        // Give TCP stack a moment to reflect disconnect
        await Task.Delay(50);

        // Next namespace call should automatically reconnect and maintain existing session
        var created = await client.CreateFolderAsync("/projects/reconnected");
        Assert.Equal("/projects/reconnected", created.Path);
        Assert.True(client.IsLoggedIn);
        Assert.Equal(originalSession, client.Session);
    }

    [Fact]
    public async Task LogoutAsync_WhenCancelledOrSlow_DisconnectsAndAllowsSubsequentLogin()
    {
        await using var mock = new MockMetadataServer { DelayLogoutResponse = true };
        mock.Start();

        var client = Register(new DfsClient(mock.Address));
        await client.LoginAsync("alice", "secret");
        Assert.True(client.IsLoggedIn);

        // Cancel logout midway to trigger socket disconnection
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.LogoutAsync(cts.Token));

        // Client local state is dropped
        Assert.False(client.IsLoggedIn);
        Assert.Null(client.Session);

        // Subsequent login on the same client must succeed because dirty/delayed connection was disconnected
        var loginRes = await client.LoginAsync("alice", "secret");
        Assert.True(client.IsLoggedIn);
        Assert.Equal("mock-session-123", loginRes.Session);
    }

    // -------------------------------------------------------------
    // Helper Mock Server
    // -------------------------------------------------------------

    private sealed class MockMetadataServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly List<TcpClient> _activeClients = new();
        private readonly object _clientsLock = new();
        private CancellationTokenSource? _cts;
        private Task? _loopTask;

        public ErrorCode? RejectWith { get; set; }
        public bool RejectHelloWithVersionMismatch { get; set; }
        public bool DelayLogoutResponse { get; set; }
        public TimeSpan? SessionDuration { get; set; }
        public string Address => $"127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";

        public void Start()
        {
            _listener.Start();
            _cts = new CancellationTokenSource();
            _loopTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
        }

        public void CloseAllClients()
        {
            lock (_clientsLock)
            {
                foreach (var c in _activeClients)
                {
                    try { c.Close(); } catch { }
                }
                _activeClients.Clear();
            }
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                    lock (_clientsLock)
                    {
                        _activeClients.Add(client);
                    }

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await HandleClientAsync(client, ct).ConfigureAwait(false);
                        }
                        finally
                        {
                            lock (_clientsLock)
                            {
                                _activeClients.Remove(client);
                            }
                        }
                    }, CancellationToken.None);
                }
                catch when (ct.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
        {
            using (client)
            await using (var stream = client.GetStream())
            {
                if (RejectHelloWithVersionMismatch)
                {
                    var helloRaw = await FrameCodec.ReadRawFrameAsync(stream, ct).ConfigureAwait(false);
                    if (helloRaw is not null)
                    {
                        var err = new ErrorResponse("hello-err", ErrorCode.VersionMismatch, "Server requires protocol version 999.");
                        await FrameCodec.WriteMessageAsync(stream, MessageType.Error, err, ct).ConfigureAwait(false);
                    }
                    return;
                }

                var hello = await HandshakeHandler.ServerHandshakeAsync(stream, "mock-meta", NodeType.Metadata, ct);
                if (hello is null) return;

                while (!ct.IsCancellationRequested)
                {
                    var frame = await FrameCodec.ReadRawFrameAsync(stream, ct).ConfigureAwait(false);
                    if (frame is null) break;

                    if (RejectWith.HasValue)
                    {
                        var err = new ErrorResponse("req-err", RejectWith.Value, "Rejected by mock server.");
                        await FrameCodec.WriteMessageAsync(stream, MessageType.Error, err, ct).ConfigureAwait(false);
                        continue;
                    }

                    switch (frame.Type)
                    {
                        case MessageType.Login:
                            var loginReq = FrameSerialization.DeserializeJson<Login>(frame.Payload);
                            var expiresAt = SessionDuration.HasValue ? DateTime.UtcNow.Add(SessionDuration.Value) : DateTime.UtcNow.AddHours(1);
                            var loginRes = new LoginResponse(loginReq.RequestId, "mock-session-123", expiresAt,
                                new UserRef("u-1", loginReq.Username), new ValueList<string>(new[] { "Admin" }), new ValueList<string>(new[] { "item.list" }));
                            await FrameCodec.WriteMessageAsync(stream, MessageType.LoginResponse, loginRes, ct).ConfigureAwait(false);
                            break;

                        case MessageType.Logout:
                            var logoutReq = FrameSerialization.DeserializeJson<Logout>(frame.Payload);
                            if (DelayLogoutResponse)
                            {
                                await Task.Delay(300, ct).ConfigureAwait(false);
                            }
                            await FrameCodec.WriteMessageAsync(stream, MessageType.LogoutResponse, new LogoutResponse(logoutReq.RequestId), ct).ConfigureAwait(false);
                            break;

                        case MessageType.CreateFolder:
                            var cfReq = FrameSerialization.DeserializeJson<CreateFolder>(frame.Payload);
                            var folder = new ItemInfo("item-f", cfReq.Path, "new", ItemKind.Folder, 0, "u-1", 0, DateTime.UtcNow);
                            await FrameCodec.WriteMessageAsync(stream, MessageType.CreateFolderResponse, new CreateFolderResponse(cfReq.RequestId, folder), ct).ConfigureAwait(false);
                            break;

                        case MessageType.Rename:
                            var renReq = FrameSerialization.DeserializeJson<Rename>(frame.Payload);
                            var renamed = new ItemInfo("item-f", "/projects/renamed", renReq.NewName, ItemKind.Folder, 0, "u-1", 0, DateTime.UtcNow);
                            await FrameCodec.WriteMessageAsync(stream, MessageType.RenameResponse, new RenameResponse(renReq.RequestId, renamed), ct).ConfigureAwait(false);
                            break;

                        case MessageType.Move:
                            var movReq = FrameSerialization.DeserializeJson<Move>(frame.Payload);
                            var moved = new ItemInfo("item-f", "/archive/renamed", "renamed", ItemKind.Folder, 0, "u-1", 0, DateTime.UtcNow);
                            await FrameCodec.WriteMessageAsync(stream, MessageType.MoveResponse, new MoveResponse(movReq.RequestId, moved), ct).ConfigureAwait(false);
                            break;

                        case MessageType.DeleteItem:
                            var delReq = FrameSerialization.DeserializeJson<DeleteItem>(frame.Payload);
                            await FrameCodec.WriteMessageAsync(stream, MessageType.DeleteItemResponse, new DeleteItemResponse(delReq.RequestId), ct).ConfigureAwait(false);
                            break;

                        default:
                            var unsupp = new ErrorResponse("req-unsupp", ErrorCode.BadRequest, "Unsupported by mock.");
                            await FrameCodec.WriteMessageAsync(stream, MessageType.Error, unsupp, ct).ConfigureAwait(false);
                            break;
                    }
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts?.Cancel();
            _listener.Stop();
            if (_loopTask is not null)
            {
                try
                {
                    await _loopTask.ConfigureAwait(false);
                }
                catch
                {
                }
            }
            _cts?.Dispose();
        }
    }
}

[Collection("EnvironmentVariables")]
public class DfsClientEnvironmentTests
{
    [Fact]
    public void Options_FromEnvironment_FallsBackToDefaultWhenUnset()
    {
        var original = Environment.GetEnvironmentVariable("METADATA_ENDPOINTS");
        try
        {
            Environment.SetEnvironmentVariable("METADATA_ENDPOINTS", null);
            var options = DfsClientOptions.FromEnvironment();
            Assert.Equal(new[] { DfsClientOptions.DefaultEndpoint }, options.MetadataEndpoints);

            Environment.SetEnvironmentVariable("METADATA_ENDPOINTS", "10.0.0.5:8000, 10.0.0.6:8000");
            var custom = DfsClientOptions.FromEnvironment();
            Assert.Equal(new[] { "10.0.0.5:8000", "10.0.0.6:8000" }, custom.MetadataEndpoints);
        }
        finally
        {
            Environment.SetEnvironmentVariable("METADATA_ENDPOINTS", original);
        }
    }
}
