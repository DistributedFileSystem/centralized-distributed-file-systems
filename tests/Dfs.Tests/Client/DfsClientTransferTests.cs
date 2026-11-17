using System.Security.Cryptography;
using Dfs.Client.Core;
using Dfs.DataServer.Config;
using Dfs.DataServer.Hosting;
using Dfs.DataServer.Storage;
using Dfs.MetadataServer.Config;
using Dfs.MetadataServer.Hosting;
using Dfs.MetadataServer.Store;
using Dfs.Shared.Commands;
using Dfs.Shared.Messages;

namespace Dfs.Tests.Client;

public sealed class DfsClientTransferTests : IAsyncDisposable
{
    private const string TokenKey = "client-transfer-e2e-token-secret-12345";
    private readonly string _metaDir;
    private readonly string _dataDir;
    private readonly MetadataStore _store;
    private readonly MetadataServerConfig _metaConfig;
    private readonly ChunkStorage _storage;
    private readonly DataServerConfig _dataConfig;

    private MetadataServerHost? _metaHost;
    private DataServerHost? _dataHost;
    private DfsClient? _client;

    public DfsClientTransferTests()
    {
        var id = Guid.NewGuid().ToString("N");
        _metaDir = Path.Combine(Path.GetTempPath(), $"dfs-meta-e2e-{id}");
        _dataDir = Path.Combine(Path.GetTempPath(), $"dfs-data-e2e-{id}");

        _metaConfig = new MetadataServerConfig(
            NodeRole: "metadata",
            NodeId: "meta-e2e",
            ListenPort: 0,
            AdvertiseHost: "127.0.0.1",
            MetadataEndpoints: ["127.0.0.1:7000"],
            DataDir: _metaDir,
            TokenKey: TokenKey,
            AdminUser: "admin",
            AdminPassword: "admin-pass-123");

        _store = new MetadataStore(_metaConfig.OpLogPath, _metaConfig.DatabasePath);

        _storage = new ChunkStorage(_dataDir, configuredCapacity: 50L * 1024 * 1024 * 1024);
        _dataConfig = new DataServerConfig(
            NodeRole: "data",
            NodeId: "ds-e2e-1",
            ListenPort: 0,
            AdvertiseHost: "127.0.0.1",
            MetadataEndpoints: ["127.0.0.1:7000"],
            DataDir: _dataDir,
            TokenKey: TokenKey,
            CapacityBytes: 50L * 1024 * 1024 * 1024);
    }

    private async Task InitializeClusterAsync()
    {
        _metaHost = new MetadataServerHost(_metaConfig, _store);
        await _metaHost.StartAsync();

        _dataHost = new DataServerHost(_dataConfig, _storage);
        await _dataHost.StartAsync();

        // Register Data Server in Metadata Store with actual bound port
        var dataAddress = $"127.0.0.1:{_dataHost.BoundPort}";
        await _store.SubmitAsync(new RegisterDataServer(
            _dataConfig.NodeId,
            dataAddress,
            _dataConfig.CapacityBytes,
            40L * 1024 * 1024 * 1024,
            DateTime.UtcNow));

        var clientOptions = new DfsClientOptions
        {
            MetadataEndpoints = [$"127.0.0.1:{_metaHost.BoundPort}"],
            ConnectTimeout = TimeSpan.FromSeconds(5),
            RequestTimeout = TimeSpan.FromSeconds(10)
        };

        _client = new DfsClient(clientOptions);
        await _client.LoginAsync(_metaConfig.AdminUser, _metaConfig.AdminPassword);
    }

    [Fact]
    public async Task MultiChunkUpload_And_Download_PreservesContentAndChecksums()
    {
        await InitializeClusterAsync();
        Assert.NotNull(_client);

        // Create 5 MB of random payload (> 4 MB chunk size -> 2 chunks)
        var payloadSize = 5 * 1024 * 1024;
        var originalBytes = RandomNumberGenerator.GetBytes(payloadSize);
        var originalSha = Convert.ToHexStringLower(SHA256.HashData(originalBytes));

        var progressReports = new List<(long Transferred, long Total)>();
        var progress = new Progress<(long Transferred, long Total)>(p => progressReports.Add(p));

        // Upload
        using var uploadStream = new MemoryStream(originalBytes);
        var commitRes = await _client.UploadFileAsync("/test-5mb.bin", uploadStream, 0, progress);

        Assert.NotNull(commitRes);
        Assert.Equal(1, commitRes.Version);
        Assert.Equal(payloadSize, commitRes.Item.Size);

        // Download
        using var downloadStream = new MemoryStream();
        await _client.DownloadFileAsync("/test-5mb.bin", downloadStream);

        var downloadedBytes = downloadStream.ToArray();
        Assert.Equal(payloadSize, downloadedBytes.Length);
        Assert.Equal(originalBytes, downloadedBytes);

        var downloadedSha = Convert.ToHexStringLower(SHA256.HashData(downloadedBytes));
        Assert.Equal(originalSha, downloadedSha);
    }

    [Fact]
    public async Task StaleBaseVersion_ThrowsConflict_AndNewVersionSucceeds()
    {
        await InitializeClusterAsync();
        Assert.NotNull(_client);

        var contentV1 = "Version 1 file content"u8.ToArray();
        using var stream1 = new MemoryStream(contentV1);
        var res1 = await _client.UploadFileAsync("/versioned.txt", stream1, 0);
        Assert.Equal(1, res1.Version);

        // Uploading again with baseVersion = 0 must fail with Conflict
        var contentV2 = "Version 2 new file content"u8.ToArray();
        using var stream2Stale = new MemoryStream(contentV2);
        var ex = await Assert.ThrowsAsync<DfsException>(() =>
            _client.UploadFileAsync("/versioned.txt", stream2Stale, 0));
        Assert.Equal(ErrorCode.Conflict, ex.Code);

        // Uploading with baseVersion = 1 must succeed and produce version 2
        using var stream2Valid = new MemoryStream(contentV2);
        var res2 = await _client.UploadFileAsync("/versioned.txt", stream2Valid, 1);
        Assert.Equal(2, res2.Version);

        // Download version 1
        using var streamV1 = new MemoryStream();
        await _client.DownloadFileAsync("/versioned.txt", streamV1, version: 1);
        Assert.Equal(contentV1, streamV1.ToArray());

        // Download version 2
        using var streamV2 = new MemoryStream();
        await _client.DownloadFileAsync("/versioned.txt", streamV2, version: 2);
        Assert.Equal(contentV2, streamV2.ToArray());
    }

    [Fact]
    public async Task EmptyFile_UploadAndDownload_WorksCleanly()
    {
        await InitializeClusterAsync();
        Assert.NotNull(_client);

        var emptyBytes = Array.Empty<byte>();
        using var uploadStream = new MemoryStream(emptyBytes);
        var commitRes = await _client.UploadFileAsync("/empty.txt", uploadStream, 0);

        Assert.Equal(1, commitRes.Version);
        Assert.Equal(0, commitRes.Item.Size);

        using var downloadStream = new MemoryStream();
        await _client.DownloadFileAsync("/empty.txt", downloadStream);

        Assert.Empty(downloadStream.ToArray());
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }
        if (_metaHost is not null)
        {
            await _metaHost.DisposeAsync();
        }
        if (_dataHost is not null)
        {
            await _dataHost.DisposeAsync();
        }
        _store.Dispose();
        _storage.Dispose();

        try
        {
            if (Directory.Exists(_metaDir)) Directory.Delete(_metaDir, true);
            if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, true);
        }
        catch
        {
        }
    }
}
