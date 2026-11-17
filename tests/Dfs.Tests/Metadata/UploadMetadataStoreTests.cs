using System.Security.Cryptography;
using Dfs.MetadataServer.Store;
using Dfs.Shared.Commands;
using Dfs.Shared.Messages;
using Dfs.Shared.Metadata;
using CommandBeginUpload = Dfs.Shared.Commands.BeginUpload;
using CommandCommitUpload = Dfs.Shared.Commands.CommitUpload;
using CommandAbortUpload = Dfs.Shared.Commands.AbortUpload;

namespace Dfs.Tests.Metadata;

public sealed class UploadMetadataStoreTests : IDisposable
{
    private const string RootId = "root";
    private readonly string _tempDir;
    private readonly MetadataStore _store;

    public UploadMetadataStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dfs-upload-store-tests-" + Guid.NewGuid().ToString("N"));
        var dbPath = Path.Combine(_tempDir, "metadata.db");
        var oplogPath = Path.Combine(_tempDir, "oplog.jsonl");
        _store = new MetadataStore(oplogPath, dbPath);
    }

    public void Dispose()
    {
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

    [Fact]
    public async Task BeginUpload_PersistsUploadRecord_AndCanBeRetrieved()
    {
        var uploadId = "up-test-1";
        var itemId = "item-file-1";
        var planned = new PlannedChunk(0, "c-chunk-1", new ValueList<string>(["127.0.0.1:7001"]));
        var cmd = new CommandBeginUpload(
            uploadId,
            itemId,
            RootId,
            "test.txt",
            "user-1",
            100,
            0,
            4096,
            new ValueList<PlannedChunk>([planned]),
            DateTime.UtcNow);

        var result = await _store.SubmitAsync(cmd);
        Assert.True(result.Success);

        var upload = await _store.FindUploadAsync(uploadId);
        Assert.NotNull(upload);
        Assert.Equal(uploadId, upload.UploadId);
        Assert.Equal(itemId, upload.ItemId);
        Assert.Equal("test.txt", upload.Name);
        Assert.Equal(100, upload.Size);
        Assert.Equal(0, upload.BaseVersion);
        Assert.Single(upload.Chunks);
        Assert.Equal("c-chunk-1", upload.Chunks[0].ChunkId);
    }

    [Fact]
    public async Task BeginUpload_ConflictWhenFileExistsAndBaseVersionMismatch()
    {
        var uploadId1 = "up-init";
        var itemId = "item-conflict-file";
        var planned1 = new PlannedChunk(0, "c-1", new ValueList<string>(["127.0.0.1:7001"]));
        await _store.SubmitAsync(new CommandBeginUpload(uploadId1, itemId, RootId, "doc.txt", "user-1", 50, 0, 4096, new ValueList<PlannedChunk>([planned1]), DateTime.UtcNow));
        await _store.SubmitAsync(new CommandCommitUpload(uploadId1, new ValueList<ChecksumInfo>([new ChecksumInfo("c-1", 50, new string('a', 64))]), 0, DateTime.UtcNow));

        var item = await _store.FindItemByIdAsync(itemId);
        Assert.NotNull(item);
        Assert.Equal(1, item.CurrentVersion);

        // Try BeginUpload with stale baseVersion = 0
        var uploadId2 = "up-stale";
        var cmdStale = new CommandBeginUpload(uploadId2, itemId, RootId, "doc.txt", "user-1", 60, 0, 4096, new ValueList<PlannedChunk>([planned1]), DateTime.UtcNow);
        var resultStale = await _store.SubmitAsync(cmdStale);

        Assert.False(resultStale.Success);
        Assert.Equal(ErrorCode.Conflict, resultStale.Error);
        Assert.NotNull(resultStale.Details);
        Assert.True(resultStale.Details.ContainsKey("currentVersion"));
        Assert.Equal(1, resultStale.Details["currentVersion"].GetInt32());
    }

    [Fact]
    public async Task CommitUpload_CreatesFileAndVersion_AndInsertsChunks()
    {
        var uploadId = "up-commit-new";
        var itemId = "item-file-new";
        var planned1 = new PlannedChunk(0, "c-commit-1", new ValueList<string>(["127.0.0.1:7001"]));
        var planned2 = new PlannedChunk(1, "c-commit-2", new ValueList<string>(["127.0.0.1:7001"]));

        // Register a data server first
        await _store.SubmitAsync(new RegisterDataServer("ds-1", "127.0.0.1:7001", 10000, 10000, DateTime.UtcNow));

        await _store.SubmitAsync(new CommandBeginUpload(
            uploadId,
            itemId,
            RootId,
            "report.pdf",
            "user-1",
            8000,
            0,
            4096,
            new ValueList<PlannedChunk>([planned1, planned2]),
            DateTime.UtcNow));

        var sha1 = Convert.ToHexStringLower(SHA256.HashData("chunk 1"u8));
        var sha2 = Convert.ToHexStringLower(SHA256.HashData("chunk 2"u8));
        var checksums = new ValueList<ChecksumInfo>([
            new ChecksumInfo("c-commit-1", 4096, sha1),
            new ChecksumInfo("c-commit-2", 3904, sha2)
        ]);

        var commitResult = await _store.SubmitAsync(new CommandCommitUpload(uploadId, checksums, 0, DateTime.UtcNow));
        Assert.True(commitResult.Success);

        // Upload record should be deleted
        var upload = await _store.FindUploadAsync(uploadId);
        Assert.Null(upload);

        // Item should now exist with version 1
        var item = await _store.FindItemByIdAsync(itemId);
        Assert.NotNull(item);
        Assert.Equal(1, item.CurrentVersion);
        Assert.Equal(8000, item.Size);
        Assert.Equal("report.pdf", item.Name);

        // Check version chunks
        var chunks = await _store.GetVersionChunksAsync(itemId, 1);
        Assert.Equal(2, chunks.Count);
        Assert.Equal("c-commit-1", chunks[0].ChunkId);
        Assert.Equal(4096, chunks[0].Size);
        Assert.Equal(sha1, chunks[0].Sha256);
        Assert.Contains("127.0.0.1:7001", chunks[0].Servers);

        // Check versions list
        var versions = await _store.ListVersionsAsync(itemId);
        Assert.Single(versions);
        Assert.Equal(1, versions[0].Version);
        Assert.Equal(8000, versions[0].Size);
        Assert.Equal(2, versions[0].ChunkCount);
    }

    [Fact]
    public async Task AbortUpload_RemovesUploadRecord()
    {
        var uploadId = "up-abort-me";
        var itemId = "item-abort";
        var planned = new PlannedChunk(0, "c-abort", new ValueList<string>(["127.0.0.1:7001"]));

        await _store.SubmitAsync(new CommandBeginUpload(uploadId, itemId, RootId, "abort.txt", "user-1", 10, 0, 4096, new ValueList<PlannedChunk>([planned]), DateTime.UtcNow));
        Assert.NotNull(await _store.FindUploadAsync(uploadId));

        var abortResult = await _store.SubmitAsync(new CommandAbortUpload(uploadId, DateTime.UtcNow));
        Assert.True(abortResult.Success);
        Assert.Null(await _store.FindUploadAsync(uploadId));
    }
}
