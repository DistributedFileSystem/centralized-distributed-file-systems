using System.Net.Sockets;
using Dfs.MetadataServer.Config;
using Dfs.MetadataServer.Hosting;
using Dfs.Shared.Framing;
using Dfs.Shared.Messages;

namespace Dfs.Tests.Metadata;

public class MetadataServerHostTests : IDisposable
{
    private readonly string _tempDir;
    private readonly List<MetadataServerHost> _hosts = new();

    public MetadataServerHostTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dfs-meta-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        foreach (var host in _hosts)
        {
            host.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch (IOException)
        {
        }
    }

    private MetadataServerHost CreateHost(int port = 0, string nodeId = "meta-test")
    {
        var config = new MetadataServerConfig(
            NodeRole: "metadata",
            NodeId: nodeId,
            ListenPort: port,
            AdvertiseHost: "127.0.0.1",
            MetadataEndpoints: new[] { $"127.0.0.1:{port}" },
            DataDir: _tempDir,
            TokenKey: "test-token-key",
            AdminUser: "admin",
            AdminPassword: "pwd");

        var host = new MetadataServerHost(config);
        _hosts.Add(host);
        return host;
    }

    [Fact]
    public async Task Host_StartsOnEphemeralPort_AndAcceptsHello()
    {
        var host = CreateHost();
        await host.StartAsync();

        Assert.True(host.IsRunning);
        Assert.True(host.BoundPort > 0);

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", host.BoundPort);
        await using var stream = client.GetStream();

        var hello = new Hello(Guid.NewGuid().ToString("D"), 1, NodeType.Client);
        var response = await HandshakeHandler.ClientHandshakeAsync(stream, hello);

        Assert.NotNull(response);
        Assert.Equal(hello.RequestId, response.RequestId);
        Assert.Equal(1, response.ProtocolVersion);
        Assert.Equal(NodeType.Metadata, response.NodeType);
        Assert.Equal("meta-test", response.NodeId);
    }

    [Fact]
    public async Task Host_RejectsClient_WithVersionMismatch()
    {
        var host = CreateHost();
        await host.StartAsync();

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", host.BoundPort);
        await using var stream = client.GetStream();

        var wrongHello = new Hello(Guid.NewGuid().ToString("D"), 99, NodeType.Client);
        var ex = await Assert.ThrowsAsync<DfsException>(() =>
            HandshakeHandler.ClientHandshakeAsync(stream, wrongHello));

        Assert.Equal(ErrorCode.VersionMismatch, ex.Code);
    }

    [Fact]
    public async Task Host_RejectsClient_WhenMessageSentBeforeHello()
    {
        var host = CreateHost();
        await host.StartAsync();

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", host.BoundPort);
        await using var stream = client.GetStream();

        var login = new Login(Guid.NewGuid().ToString("D"), "an.nguyen", "secret");
        await FrameCodec.WriteMessageAsync(stream, MessageType.Login, login);

        var responseFrame = await FrameCodec.ReadRawFrameAsync(stream);
        Assert.NotNull(responseFrame);
        Assert.Equal(MessageType.Error, responseFrame.Type);

        var error = FrameSerialization.DeserializeJson<ErrorResponse>(responseFrame.Payload);
        Assert.Equal(ErrorCode.BadRequest, error.Code);
    }

    [Fact]
    public async Task Host_StopsGracefully()
    {
        var host = CreateHost();
        await host.StartAsync();
        Assert.True(host.IsRunning);

        await host.StopAsync();
        Assert.False(host.IsRunning);
    }
}
