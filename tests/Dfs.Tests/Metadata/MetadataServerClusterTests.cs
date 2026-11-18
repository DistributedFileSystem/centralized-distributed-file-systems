using System.Net.Sockets;
using Dfs.MetadataServer.Config;
using Dfs.MetadataServer.Hosting;
using Dfs.MetadataServer.Store;
using Dfs.Shared.Framing;
using Dfs.Shared.Messages;
using Dfs.Shared.Security;

namespace Dfs.Tests.Metadata;

public sealed class MetadataServerClusterTests : IAsyncDisposable
{
    private readonly string _tempDir;
    private readonly MetadataServerConfig _config;
    private readonly TestTimeProvider _time;
    private readonly MetadataStore _store;
    private MetadataServerHost? _host;

    public MetadataServerClusterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dfs-meta-cluster-tests-" + Guid.NewGuid().ToString("N"));
        _config = new MetadataServerConfig(
            NodeRole: "metadata",
            NodeId: "m1",
            ListenPort: 0,
            AdvertiseHost: "127.0.0.1",
            MetadataEndpoints: ["127.0.0.1:7000"],
            DataDir: _tempDir,
            TokenKey: "cluster-test-secret-key-12345",
            AdminUser: "admin",
            AdminPassword: "admin-password-123");

        _time = new TestTimeProvider(new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero));
        _store = new MetadataStore(_config.OpLogPath, _config.DatabasePath);
    }

    private async Task<MetadataServerHost> StartHostAsync(IAuthorizer? authorizer = null)
    {
        _host = new MetadataServerHost(_config, _store, authorizer, _time);
        await _host.StartAsync();
        return _host;
    }

    private async Task<(TcpClient Client, NetworkStream Stream)> ConnectAndHandshakeAsync(MetadataServerHost host)
    {
        var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", host.BoundPort);
        var stream = client.GetStream();

        var hello = new Hello(
            Guid.NewGuid().ToString("D"),
            ProtocolConstants.CurrentProtocolVersion,
            NodeType.Data);

        var response = await HandshakeHandler.ClientHandshakeAsync(stream, hello);
        Assert.Equal(ProtocolConstants.CurrentProtocolVersion, response.ProtocolVersion);
        Assert.Equal(NodeType.Metadata, response.NodeType);

        return (client, stream);
    }

    [Fact]
    public async Task Register_Succeeds_AndSetsNodeAlive()
    {
        var host = await StartHostAsync();
        var (client, stream) = await ConnectAndHandshakeAsync(host);
        using (client)
        await using (stream)
        {
            var register = new Register(
                Guid.NewGuid().ToString("D"),
                "ds-1",
                "192.168.10.13:7001",
                100L * 1024 * 1024 * 1024,
                80L * 1024 * 1024 * 1024);

            await FrameCodec.WriteMessageAsync(stream, MessageType.Register, register);
            var response = await FrameCodec.ReadMessageAsync<RegisterResponse>(stream, MessageType.RegisterResponse);

            Assert.NotNull(response);
            Assert.Equal(register.RequestId, response.RequestId);
            Assert.Equal(3, response.HeartbeatIntervalSeconds);

            var servers = await host.Store.ListDataServersAsync();
            Assert.Single(servers);
            var server = servers[0];
            Assert.Equal("ds-1", server.NodeId);
            Assert.Equal("192.168.10.13:7001", server.Address);
            Assert.Equal(DataServerStatus.Alive, server.Status);
            Assert.Equal(register.CapacityBytes, server.CapacityBytes);
            Assert.Equal(register.FreeBytes, server.FreeBytes);
            Assert.Equal(0, server.ChunkCount);
        }
    }

    [Fact]
    public async Task Register_UpdatesExistingNodeAddress_AndCapacity()
    {
        var host = await StartHostAsync();
        var (client, stream) = await ConnectAndHandshakeAsync(host);
        using (client)
        await using (stream)
        {
            var reg1 = new Register(Guid.NewGuid().ToString("D"), "ds-1", "192.168.10.13:7001", 100, 80);
            await FrameCodec.WriteMessageAsync(stream, MessageType.Register, reg1);
            await FrameCodec.ReadMessageAsync<RegisterResponse>(stream, MessageType.RegisterResponse);

            // Re-register with new address
            var reg2 = new Register(Guid.NewGuid().ToString("D"), "ds-1", "192.168.10.99:7001", 200, 150);
            await FrameCodec.WriteMessageAsync(stream, MessageType.Register, reg2);
            await FrameCodec.ReadMessageAsync<RegisterResponse>(stream, MessageType.RegisterResponse);

            var servers = await host.Store.ListDataServersAsync();
            Assert.Single(servers);
            var server = servers[0];
            Assert.Equal("192.168.10.99:7001", server.Address);
            Assert.Equal(200, server.CapacityBytes);
            Assert.Equal(150, server.FreeBytes);
            Assert.Equal(DataServerStatus.Alive, server.Status);
        }
    }

    [Fact]
    public async Task Heartbeat_Succeeds_AndUpdatesMetrics()
    {
        var host = await StartHostAsync();
        var (client, stream) = await ConnectAndHandshakeAsync(host);
        using (client)
        await using (stream)
        {
            var reg = new Register(Guid.NewGuid().ToString("D"), "ds-1", "127.0.0.1:7001", 1000, 800);
            await FrameCodec.WriteMessageAsync(stream, MessageType.Register, reg);
            await FrameCodec.ReadMessageAsync<RegisterResponse>(stream, MessageType.RegisterResponse);

            _time.Advance(TimeSpan.FromSeconds(3));

            var hb = new Heartbeat(Guid.NewGuid().ToString("D"), "ds-1", FreeBytes: 750, ChunkCount: 25);
            await FrameCodec.WriteMessageAsync(stream, MessageType.Heartbeat, hb);
            var response = await FrameCodec.ReadMessageAsync<HeartbeatResponse>(stream, MessageType.HeartbeatResponse);

            Assert.NotNull(response);
            Assert.Equal(hb.RequestId, response.RequestId);

            var servers = await host.Store.ListDataServersAsync();
            var server = servers.Single(s => s.NodeId == "ds-1");
            Assert.Equal(750, server.FreeBytes);
            Assert.Equal(25, server.ChunkCount);
            Assert.Equal(_time.GetUtcNow().UtcDateTime, server.LastHeartbeat);
            Assert.Equal(DataServerStatus.Alive, server.Status);
        }
    }

    [Fact]
    public async Task Heartbeat_UnregisteredNode_ReturnsNotFound()
    {
        var host = await StartHostAsync();
        var (client, stream) = await ConnectAndHandshakeAsync(host);
        using (client)
        await using (stream)
        {
            var hb = new Heartbeat(Guid.NewGuid().ToString("D"), "ds-unregistered", 100, 5);
            await FrameCodec.WriteMessageAsync(stream, MessageType.Heartbeat, hb);

            var ex = await Assert.ThrowsAsync<DfsException>(() =>
                FrameCodec.ReadMessageAsync<HeartbeatResponse>(stream, MessageType.HeartbeatResponse));

            Assert.Equal(ErrorCode.NotFound, ex.Code);
        }
    }

    [Fact]
    public async Task HeartbeatMonitor_TransitionsDeadNode_After9Seconds_AndRecoversOnHeartbeat()
    {
        var host = await StartHostAsync();
        var (client, stream) = await ConnectAndHandshakeAsync(host);
        using (client)
        await using (stream)
        {
            var reg = new Register(Guid.NewGuid().ToString("D"), "ds-1", "127.0.0.1:7001", 1000, 800);
            await FrameCodec.WriteMessageAsync(stream, MessageType.Register, reg);
            await FrameCodec.ReadMessageAsync<RegisterResponse>(stream, MessageType.RegisterResponse);

            // Initially Alive
            var servers = await host.Store.ListDataServersAsync();
            Assert.Equal(DataServerStatus.Alive, servers[0].Status);

            // 8 seconds pass -> still alive
            _time.Advance(TimeSpan.FromSeconds(8));
            host.CheckDeadServers();
            servers = await host.Store.ListDataServersAsync();
            Assert.Equal(DataServerStatus.Alive, servers[0].Status);

            // Total 10 seconds pass (> 9s threshold) -> transitions to Dead
            _time.Advance(TimeSpan.FromSeconds(2));
            var markedDead = host.CheckDeadServers();
            Assert.Equal(1, markedDead);

            servers = await host.Store.ListDataServersAsync();
            Assert.Equal(DataServerStatus.Dead, servers[0].Status);

            // Now send a heartbeat -> recovers back to Alive!
            var hb = new Heartbeat(Guid.NewGuid().ToString("D"), "ds-1", 700, 10);
            await FrameCodec.WriteMessageAsync(stream, MessageType.Heartbeat, hb);
            await FrameCodec.ReadMessageAsync<HeartbeatResponse>(stream, MessageType.HeartbeatResponse);

            servers = await host.Store.ListDataServersAsync();
            Assert.Equal(DataServerStatus.Alive, servers[0].Status);
        }
    }

    [Fact]
    public async Task HeartbeatMonitor_HandlesSubsecondTimestamp_Accurately()
    {
        var subsecondTime = new TestTimeProvider(new DateTimeOffset(2026, 11, 17, 12, 0, 0, 900, TimeSpan.Zero));
        var host = new MetadataServerHost(_config, _store, authorizer: null, timeProvider: subsecondTime);
        _host = host;
        await host.StartAsync();

        var (client, stream) = await ConnectAndHandshakeAsync(host);
        using (client)
        await using (stream)
        {
            var reg = new Register(Guid.NewGuid().ToString("D"), "ds-sub", "127.0.0.1:7002", 2000, 1500);
            await FrameCodec.WriteMessageAsync(stream, MessageType.Register, reg);
            await FrameCodec.ReadMessageAsync<RegisterResponse>(stream, MessageType.RegisterResponse);

            var servers = await host.Store.ListDataServersAsync();
            Assert.Equal(DataServerStatus.Alive, servers[0].Status);

            // Advance 8.1 seconds -> now at 12:00:09.000Z.
            // With string comparison in SQLite, "12:00:00.9Z" < "12:00:09Z" was wrongly evaluated to true because '.' < 'Z'.
            // With DateTime comparison in C#, 8.1s < 9s, so server must remain Alive.
            subsecondTime.Advance(TimeSpan.FromMilliseconds(8100));
            var deadCount = host.CheckDeadServers();
            Assert.Equal(0, deadCount);

            servers = await host.Store.ListDataServersAsync();
            Assert.Equal(DataServerStatus.Alive, servers[0].Status);

            // Advance another 1.0 second -> total 9.1s elapsed (> 9s cutoff) -> transitions to Dead.
            subsecondTime.Advance(TimeSpan.FromSeconds(1));
            deadCount = host.CheckDeadServers();
            Assert.Equal(1, deadCount);

            servers = await host.Store.ListDataServersAsync();
            Assert.Equal(DataServerStatus.Dead, servers[0].Status);
        }
    }

    [Fact]
    public async Task ClusterStatus_ReturnsAllRegisteredDataServers_WhenAuthenticated()
    {
        var host = await StartHostAsync();
        var (client, stream) = await ConnectAndHandshakeAsync(host);
        using (client)
        await using (stream)
        {
            // Register a data server
            var reg = new Register(Guid.NewGuid().ToString("D"), "ds-1", "127.0.0.1:7001", 1000, 800);
            await FrameCodec.WriteMessageAsync(stream, MessageType.Register, reg);
            await FrameCodec.ReadMessageAsync<RegisterResponse>(stream, MessageType.RegisterResponse);

            // Admin login
            var login = new Login(Guid.NewGuid().ToString("D"), "admin", "admin-password-123");
            await FrameCodec.WriteMessageAsync(stream, MessageType.Login, login);
            var loginResponse = await FrameCodec.ReadMessageAsync<LoginResponse>(stream, MessageType.LoginResponse);
            Assert.NotNull(loginResponse);

            // Query ClusterStatus
            var statusRequest = new ClusterStatus(Guid.NewGuid().ToString("D"), loginResponse.Session);
            await FrameCodec.WriteMessageAsync(stream, MessageType.ClusterStatus, statusRequest);

            var response = await FrameCodec.ReadMessageAsync<ClusterStatusResponse>(stream, MessageType.ClusterStatusResponse);
            Assert.NotNull(response);
            Assert.Equal(statusRequest.RequestId, response.RequestId);
            Assert.Single(response.DataServers);
            Assert.Equal("ds-1", response.DataServers[0].NodeId);
            Assert.Equal(DataServerStatus.Alive, response.DataServers[0].Status);
        }
    }

    [Fact]
    public async Task ClusterStatus_RequiresActiveSession()
    {
        var host = await StartHostAsync();
        var (client, stream) = await ConnectAndHandshakeAsync(host);
        using (client)
        await using (stream)
        {
            var statusRequest = new ClusterStatus(Guid.NewGuid().ToString("D"), "invalid-session-token");
            await FrameCodec.WriteMessageAsync(stream, MessageType.ClusterStatus, statusRequest);

            var ex = await Assert.ThrowsAsync<DfsException>(() =>
                FrameCodec.ReadMessageAsync<ClusterStatusResponse>(stream, MessageType.ClusterStatusResponse));

            Assert.Equal(ErrorCode.Unauthorized, ex.Code);
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
