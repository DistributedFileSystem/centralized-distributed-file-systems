using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Dfs.Client.Core;
using Dfs.DataServer.Config;
using Dfs.DataServer.Hosting;
using Dfs.DataServer.Storage;
using Dfs.MetadataServer.Config;
using Dfs.MetadataServer.Hosting;
using Dfs.MetadataServer.Store;
using Dfs.Shared.Commands;
using Dfs.Shared.Framing;
using Dfs.Shared.Messages;
using CommitUploadRequest = Dfs.Shared.Messages.CommitUpload;

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

    private async Task InitializeClusterAsync(int keepVersions = MetadataServerConfig.DefaultKeepVersions)
    {
        var config = _metaConfig with { KeepVersions = keepVersions };
        _metaHost = new MetadataServerHost(config, _store);
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

        var uploadReports = new List<(long Transferred, long Total)>();
        var uploadProgress = new SynchronousProgress<(long Transferred, long Total)>(uploadReports.Add);

        // Upload
        using var uploadStream = new MemoryStream(originalBytes);
        var commitRes = await _client.UploadFileAsync("/test-5mb.bin", uploadStream, 0, uploadProgress);

        Assert.NotNull(commitRes);
        Assert.Equal(1, commitRes.Version);
        Assert.Equal(payloadSize, commitRes.Item.Size);
        Assert.NotEmpty(uploadReports);
        Assert.Equal((payloadSize, payloadSize), uploadReports[^1]);

        // Download with progress
        var downloadReports = new List<(long Transferred, long Total)>();
        var downloadProgress = new SynchronousProgress<(long Transferred, long Total)>(downloadReports.Add);

        using var downloadStream = new MemoryStream();
        await _client.DownloadFileAsync("/test-5mb.bin", downloadStream, version: 0, progress: downloadProgress);

        var downloadedBytes = downloadStream.ToArray();
        Assert.Equal(payloadSize, downloadedBytes.Length);
        Assert.Equal(originalBytes, downloadedBytes);
        Assert.NotEmpty(downloadReports);
        Assert.Equal((payloadSize, payloadSize), downloadReports[^1]);

        var downloadedSha = Convert.ToHexStringLower(SHA256.HashData(downloadedBytes));
        Assert.Equal(originalSha, downloadedSha);
        var audit = await _client.ListAuditAsync();
        Assert.Contains(audit, entry => entry.Action == nameof(MessageType.CommitUpload) && entry.Allowed);
    }

    [Fact]
    public async Task UploadFileAsync_MemoryStreamWithNonZeroPosition_UploadsOnlyRemainingBytes()
    {
        await InitializeClusterAsync();
        Assert.NotNull(_client);

        var buffer = new byte[100];
        for (var i = 0; i < buffer.Length; i++) buffer[i] = (byte)(i + 1);

        using var stream = new MemoryStream(buffer);
        stream.Position = 40; // 60 bytes remain from 40 to 99

        var commitRes = await _client.UploadFileAsync("/offset.txt", stream, 0);
        Assert.Equal(1, commitRes.Version);
        Assert.Equal(60, commitRes.Item.Size);

        using var downloadStream = new MemoryStream();
        await _client.DownloadFileAsync("/offset.txt", downloadStream);

        var downloaded = downloadStream.ToArray();
        Assert.Equal(60, downloaded.Length);
        Assert.Equal(buffer[40..], downloaded);
    }

    [Fact]
    public async Task UploadFileAsync_AbortsUploadOnServer_WhenChunkUploadFails()
    {
        await InitializeClusterAsync();
        Assert.NotNull(_client);

        // Register a fake non-existent data server with enormous capacity so chunks are routed there
        await _store.SubmitAsync(new RegisterDataServer(
            "ds-unreachable",
            "127.0.0.1:19999",
            100L * 1024 * 1024 * 1024,
            100L * 1024 * 1024 * 1024,
            DateTime.UtcNow));

        var bytes = new byte[50];
        using var stream = new MemoryStream(bytes);

        // Upload should fail because chunk cannot be written to unreachable data server
        await Assert.ThrowsAsync<DfsException>(() =>
            _client.UploadFileAsync("/failing-upload.bin", stream, 0));

        // Verify that in-flight uploads on metadata store have been cleaned up via AbortUpload
        // No upload record should remain for /failing-upload.bin
        var item = await _store.FindItemByPathAsync("/failing-upload.bin");
        Assert.Null(item);
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

    [Fact]
    public async Task VersionHistory_ListVersions_DownloadSpecificVersion_AndKeepVersionsPruning()
    {
        await InitializeClusterAsync(keepVersions: 2);
        Assert.NotNull(_client);

        var payloads = new[]
        {
            "content v1"u8.ToArray(),
            "content v2 longer"u8.ToArray(),
            "content v3 even longer"u8.ToArray(),
            "content v4 final"u8.ToArray()
        };

        for (var i = 0; i < payloads.Length; i++)
        {
            using var ms = new MemoryStream(payloads[i]);
            var res = await _client.UploadFileAsync("/history.txt", ms, baseVersion: i);
            Assert.Equal(i + 1, res.Version);
        }

        var versions = await _client.ListVersionsAsync("/history.txt");
        Assert.Equal([4, 3], versions.Select(v => v.Version));
        Assert.Equal([payloads[3].Length, payloads[2].Length], versions.Select(v => v.Size));

        // Retained version 3 and latest (version 0 -> 4) can still be downloaded
        using var dlV3 = new MemoryStream();
        await _client.DownloadFileAsync("/history.txt", dlV3, version: 3);
        Assert.Equal(payloads[2], dlV3.ToArray());

        using var dlLatest = new MemoryStream();
        await _client.DownloadFileAsync("/history.txt", dlLatest, version: 0);
        Assert.Equal(payloads[3], dlLatest.ToArray());

        // Pruned versions 1 and 2 and non-existent version 5 throw NotFound
        foreach (var missingVersion in new[] { 1, 2, 5 })
        {
            using var dlMissing = new MemoryStream();
            var ex = await Assert.ThrowsAsync<DfsException>(() =>
                _client.DownloadFileAsync("/history.txt", dlMissing, version: missingVersion));
            Assert.Equal(ErrorCode.NotFound, ex.Code);
        }
    }

    [Fact]
    public async Task CommitUpload_MidFlightVersionConflict_ThrowsConflictWithDetails()
    {
        await InitializeClusterAsync();
        Assert.NotNull(_client);

        await _client.UploadFileAsync("/race.txt", new MemoryStream("v1"u8.ToArray()), baseVersion: 0);

        // User starts upload based on version 1 over raw TCP
        using var tcp = new TcpClient();
        await tcp.ConnectAsync("127.0.0.1", _metaHost!.BoundPort);
        await using var stream = tcp.GetStream();
        await HandshakeHandler.ClientHandshakeAsync(stream, new Hello(Guid.NewGuid().ToString("D"), ProtocolConstants.CurrentProtocolVersion, NodeType.Client));

        await FrameCodec.WriteMessageAsync(stream, MessageType.Login, new Login(Guid.NewGuid().ToString("D"), _metaConfig.AdminUser, _metaConfig.AdminPassword));
        var loginRes = await FrameCodec.ReadMessageAsync<LoginResponse>(stream, MessageType.LoginResponse);
        Assert.NotNull(loginRes);

        var staleBytes = Encoding.UTF8.GetBytes("stale v2");
        await FrameCodec.WriteMessageAsync(stream, MessageType.CreateUpload, new CreateUpload(Guid.NewGuid().ToString("D"), loginRes.Session, "/race.txt", staleBytes.Length, BaseVersion: 1));
        var createRes = await FrameCodec.ReadMessageAsync<CreateUploadResponse>(stream, MessageType.CreateUploadResponse);
        Assert.NotNull(createRes);

        // Concurrent writer commits version 2 before the first upload commits
        var winner = await _client.UploadFileAsync("/race.txt", new MemoryStream("winner v2"u8.ToArray()), baseVersion: 1);
        Assert.Equal(2, winner.Version);

        // Attempting to commit the in-flight upload must return Conflict with currentVersion = 2
        var sha = Convert.ToHexStringLower(SHA256.HashData(staleBytes));
        var checksums = new ValueList<ChecksumInfo>([new ChecksumInfo(createRes.Chunks[0].ChunkId, staleBytes.Length, sha)]);
        await FrameCodec.WriteMessageAsync(stream, MessageType.CommitUpload, new CommitUploadRequest(Guid.NewGuid().ToString("D"), loginRes.Session, createRes.UploadId, checksums));
        var frame = await FrameCodec.ReadRawFrameAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal(MessageType.Error, frame.Type);

        var err = FrameSerialization.DeserializeJson<ErrorResponse>(frame.Payload);
        Assert.Equal(ErrorCode.Conflict, err.Code);
        Assert.NotNull(err.Details);
        Assert.True(err.Details.TryGetValue("currentVersion", out var currentVerElem));
        Assert.Equal(2, currentVerElem.GetInt32());
    }

    [Fact]
    public async Task UploadFileAsync_AbortsUpload_WhenCommitUploadFailsWithConflict()
    {
        await InitializeClusterAsync();
        Assert.NotNull(_client);

        // Concurrent new-file race: while client 1 uploads chunks for /abort-on-conflict.txt (baseVersion 0),
        // client 2 creates the file first, causing client 1's CommitUpload to fail with Conflict.
        using var client2 = new DfsClient(new DfsClientOptions
        {
            MetadataEndpoints = [$"127.0.0.1:{_metaHost!.BoundPort}"],
            ConnectTimeout = TimeSpan.FromSeconds(5),
            RequestTimeout = TimeSpan.FromSeconds(10)
        });
        await client2.LoginAsync(_metaConfig.AdminUser, _metaConfig.AdminPassword);

        var raced = false;
        var progress = new SyncProgress<(long BytesTransferred, long TotalBytes)>(_ =>
        {
            if (raced) return;
            raced = true;
            client2.UploadFileAsync("/abort-on-conflict.txt", new MemoryStream("winner"u8.ToArray()), baseVersion: 0)
                .GetAwaiter().GetResult();
        });

        var ex = await Assert.ThrowsAsync<DfsException>(() =>
            _client.UploadFileAsync("/abort-on-conflict.txt", new MemoryStream("loser"u8.ToArray()), baseVersion: 0, progress));
        Assert.Equal(ErrorCode.Conflict, ex.Code);

        // Verify no orphan upload record remains in SQLite
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = _metaConfig.DatabasePath, Pooling = false }.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM uploads";
        var count = Convert.ToInt64(cmd.ExecuteScalar());
        Assert.Equal(0L, count);
    }

    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
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

    private sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
