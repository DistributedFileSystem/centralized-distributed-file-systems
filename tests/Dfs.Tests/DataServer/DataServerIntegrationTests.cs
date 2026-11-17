using System.Net.Sockets;
using Dfs.DataServer.Config;
using Dfs.DataServer.Hosting;
using Dfs.DataServer.Storage;
using Dfs.MetadataServer.Config;
using Dfs.MetadataServer.Hosting;
using Dfs.MetadataServer.Security;
using Dfs.MetadataServer.Store;
using Dfs.Shared.Framing;
using Dfs.Shared.Messages;

namespace Dfs.Tests.DataServer;

public sealed class DataServerIntegrationTests : IAsyncDisposable
{
    private const string SharedTokenKey = "integration-shared-token-key-dfs-2026";
    private readonly string _metaDir;
    private readonly string _dataDir;
    private readonly MetadataServerConfig _metaConfig;
    private readonly MetadataStore _metaStore;
    private readonly MetadataServerHost _metaHost;
    private readonly ChunkStorage _dataStorage;
    private DataServerHost? _dataHost;
    private HeartbeatClient? _heartbeatClient;

    public DataServerIntegrationTests()
    {
        _metaDir = Path.Combine(Path.GetTempPath(), "dfs-integ-meta-" + Guid.NewGuid().ToString("N"));
        _dataDir = Path.Combine(Path.GetTempPath(), "dfs-integ-data-" + Guid.NewGuid().ToString("N"));

        _metaConfig = new MetadataServerConfig(
            NodeRole: "metadata",
            NodeId: "m1",
            ListenPort: 0,
            AdvertiseHost: "127.0.0.1",
            MetadataEndpoints: ["127.0.0.1:7000"],
            DataDir: _metaDir,
            TokenKey: SharedTokenKey,
            AdminUser: "admin",
            AdminPassword: "admin-pass-123");

        _metaStore = new MetadataStore(_metaConfig.OpLogPath, _metaConfig.DatabasePath);
        _metaHost = new MetadataServerHost(_metaConfig, _metaStore);

        _dataStorage = new ChunkStorage(_dataDir, configuredCapacity: 50L * 1024 * 1024 * 1024);
    }

    [Fact]
    public async Task DataServer_EndToEnd_Registers_SendsHeartbeats_AndProcessesChunks()
    {
        // 1. Start MetadataServer
        await _metaHost.StartAsync();

        // 2. Start DataServer
        var initialConfig = new DataServerConfig(
            NodeRole: "data",
            NodeId: "ds-integ-1",
            ListenPort: 0,
            AdvertiseHost: "127.0.0.1",
            MetadataEndpoints: [$"127.0.0.1:{_metaHost.BoundPort}"],
            DataDir: _dataDir,
            TokenKey: SharedTokenKey,
            CapacityBytes: 50L * 1024 * 1024 * 1024);

        _dataHost = new DataServerHost(initialConfig, _dataStorage);
        await _dataHost.StartAsync();

        // 3. Start HeartbeatClient with actual bound port
        var registeredConfig = initialConfig with { ListenPort = _dataHost.BoundPort };
        _heartbeatClient = new HeartbeatClient(registeredConfig, _dataStorage, reconnectDelay: TimeSpan.FromMilliseconds(100));
        await _heartbeatClient.StartAsync();

        // Wait until registered (up to 5 seconds)
        var registered = false;
        for (var i = 0; i < 50; i++)
        {
            if (_heartbeatClient.IsRegistered)
            {
                registered = true;
                break;
            }
            await Task.Delay(100);
        }
        Assert.True(registered, "HeartbeatClient failed to register within timeout.");

        // 4. Verify MetadataServer sees DataServer via ClusterStatus
        using var metaClient = new TcpClient();
        await metaClient.ConnectAsync("127.0.0.1", _metaHost.BoundPort);
        await using var metaStream = metaClient.GetStream();

        var metaHello = new Hello(Guid.NewGuid().ToString("D"), ProtocolConstants.CurrentProtocolVersion, NodeType.Client);
        await HandshakeHandler.ClientHandshakeAsync(metaStream, metaHello);

        var login = new Login(Guid.NewGuid().ToString("D"), "admin", "admin-pass-123");
        await FrameCodec.WriteMessageAsync(metaStream, MessageType.Login, login);
        var loginResponse = await FrameCodec.ReadMessageAsync<LoginResponse>(metaStream, MessageType.LoginResponse);
        Assert.NotNull(loginResponse);

        var statusReq = new ClusterStatus(Guid.NewGuid().ToString("D"), loginResponse.Session);
        await FrameCodec.WriteMessageAsync(metaStream, MessageType.ClusterStatus, statusReq);
        var statusResp = await FrameCodec.ReadMessageAsync<ClusterStatusResponse>(metaStream, MessageType.ClusterStatusResponse);
        Assert.NotNull(statusResp);
        Assert.Single(statusResp.DataServers);

        var dsInfo = statusResp.DataServers[0];
        Assert.Equal("ds-integ-1", dsInfo.NodeId);
        Assert.Equal(DataServerStatus.Alive, dsInfo.Status);
        Assert.Equal($"127.0.0.1:{_dataHost.BoundPort}", dsInfo.Address);

        // 5. Connect directly to DataServer and write a chunk using signed token
        using var dataClient = new TcpClient();
        await dataClient.ConnectAsync("127.0.0.1", _dataHost.BoundPort);
        await using var dataStream = dataClient.GetStream();

        var dataHello = new Hello(Guid.NewGuid().ToString("D"), ProtocolConstants.CurrentProtocolVersion, NodeType.Client);
        await HandshakeHandler.ClientHandshakeAsync(dataStream, dataHello);

        var signer = new DataTokenSigner(SharedTokenKey);
        var chunkId = "c-01HINTEGCHUNK";
        var writeToken = signer.Sign("user-integ", chunkId, "w");
        var chunkData = "Distributed Chunk Storage Rocks!"u8.ToArray();

        var writeHeader = new WriteChunk(Guid.NewGuid().ToString("D"), chunkId, writeToken, chunkData.Length);
        await FrameCodec.WriteChunkFrameAsync(dataStream, MessageType.WriteChunk, writeHeader, chunkData);

        var writeResp = await FrameCodec.ReadMessageAsync<WriteChunkResponse>(dataStream, MessageType.WriteChunkResponse);
        Assert.NotNull(writeResp);
        Assert.Equal(chunkId, writeResp.ChunkId);
        Assert.Equal(chunkData.Length, writeResp.Size);

        // 6. Read the chunk back
        var readToken = signer.Sign("user-integ", chunkId, "r");
        var readReq = new ReadChunk(Guid.NewGuid().ToString("D"), chunkId, readToken);
        await FrameCodec.WriteMessageAsync(dataStream, MessageType.ReadChunk, readReq);

        var readFrame = await FrameCodec.ReadChunkFrameAsync<ReadChunkResponse>(dataStream, MessageType.ReadChunkResponse);
        Assert.NotNull(readFrame);
        Assert.Equal(chunkId, readFrame.Header.ChunkId);
        Assert.Equal(chunkData, readFrame.ChunkBytes.ToArray());
    }

    public async ValueTask DisposeAsync()
    {
        if (_heartbeatClient is not null)
        {
            await _heartbeatClient.DisposeAsync();
        }
        if (_dataHost is not null)
        {
            await _dataHost.DisposeAsync();
        }
        _dataStorage.Dispose();
        await _metaHost.DisposeAsync();
        _metaStore.Dispose();

        try
        {
            if (Directory.Exists(_metaDir)) Directory.Delete(_metaDir, recursive: true);
            if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, recursive: true);
        }
        catch
        {
        }
    }
}
