using System.Security.Cryptography;
using Dfs.MetadataServer.Security;
using Dfs.MetadataServer.Store;
using Dfs.Shared.Commands;
using Dfs.Shared.Messages;
using Dfs.Shared.Metadata;
using Dfs.Shared.Security;
using CommandBeginUpload = Dfs.Shared.Commands.BeginUpload;
using CommandCommitUpload = Dfs.Shared.Commands.CommitUpload;
using CommandAbortUpload = Dfs.Shared.Commands.AbortUpload;
using CommandCreateUser = Dfs.Shared.Commands.CreateUser;

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
        await _store.SubmitAsync(new RegisterDataServer("ds-1", "127.0.0.1:7001", 10000, 10000, DateTime.UtcNow));

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

    [Fact]
    public async Task CommitUpload_NewFile_AutomaticallyGrantsOwner_AndPreservesAcrossReplay()
    {
        await new BuiltInRoleSeeder(_store).SeedAsync();
        await _store.SubmitAsync(new RegisterDataServer("ds-1", "127.0.0.1:7001", 10000, 10000, DateTime.UtcNow));
        await _store.SubmitAsync(new CommandCreateUser("u-alice", "alice", "hash-alice", "salt", DateTime.UtcNow));
        await _store.SubmitAsync(new CommandCreateUser("u-bob", "bob", "hash-bob", "salt", DateTime.UtcNow));

        var uploadId = "up-owner-test";
        var itemId = "item-owned-file";
        var planned = new PlannedChunk(0, "c-owned-1", new ValueList<string>(["127.0.0.1:7001"]));
        var sha = Convert.ToHexStringLower(SHA256.HashData("content"u8));

        var beginRes = await _store.SubmitAsync(new CommandBeginUpload(
            uploadId, itemId, RootId, "my-owned-file.txt", "u-alice", 7, 0, 4096,
            new ValueList<PlannedChunk>([planned]), DateTime.UtcNow));
        Assert.True(beginRes.Success);

        var commitRes = await _store.SubmitAsync(new CommandCommitUpload(
            uploadId,
            new ValueList<ChecksumInfo>([new ChecksumInfo("c-owned-1", 7, sha)]),
            0,
            DateTime.UtcNow));
        Assert.True(commitRes.Success);

        var authorizer = new RbacAuthorizer(_store);
        var readAlice = await authorizer.AuthorizeAsync("u-alice", Permission.FileRead, itemId);
        var writeAlice = await authorizer.AuthorizeAsync("u-alice", Permission.FileWrite, itemId);
        var shareAlice = await authorizer.AuthorizeAsync("u-alice", Permission.ItemShare, itemId);
        var deleteAlice = await authorizer.AuthorizeAsync("u-alice", Permission.ItemDelete, itemId);

        Assert.True(readAlice.Allowed);
        Assert.True(writeAlice.Allowed);
        Assert.True(shareAlice.Allowed);
        Assert.True(deleteAlice.Allowed);

        // Bob has no permissions on Alice's file
        var readBob = await authorizer.AuthorizeAsync("u-bob", Permission.FileRead, itemId);
        Assert.False(readBob.Allowed);

        // Verify across op-log replay
        var replayedDb = Path.Combine(_tempDir, "metadata-replayed.db");
        using var replayedStore = new MetadataStore(Path.Combine(_tempDir, "oplog.jsonl"), replayedDb);
        var replayedAuthorizer = new RbacAuthorizer(replayedStore);

        var replayedReadAlice = await replayedAuthorizer.AuthorizeAsync("u-alice", Permission.FileRead, itemId);
        Assert.True(replayedReadAlice.Allowed);
        var replayedShareAlice = await replayedAuthorizer.AuthorizeAsync("u-alice", Permission.ItemShare, itemId);
        Assert.True(replayedShareAlice.Allowed);
    }

    [Fact]
    public async Task CommitUpload_OwnerGrantAlreadyExists_ReturnsAlreadyExists()
    {
        await new BuiltInRoleSeeder(_store).SeedAsync();
        await _store.SubmitAsync(new RegisterDataServer("ds-1", "127.0.0.1:7001", 10000, 10000, DateTime.UtcNow));
        await _store.SubmitAsync(new CommandCreateUser("u-alice", "alice", "hash-alice", "salt", DateTime.UtcNow));

        var uploadId = "up-collision";
        var itemId = "item-collision";
        var planned = new PlannedChunk(0, "c-col-1", new ValueList<string>(["127.0.0.1:7001"]));

        await _store.SubmitAsync(new CommandBeginUpload(
            uploadId, itemId, RootId, "col.txt", "u-alice", 10, 0, 4096,
            new ValueList<PlannedChunk>([planned]), DateTime.UtcNow));

        // Pre-insert colliding grant
        await _store.SubmitAsync(new SetGrant("g-owner-" + itemId, BuiltInRoles.OwnerId, SubjectType.User, "u-alice", ItemId: null, DateTime.UtcNow));

        var commitRes = await _store.SubmitAsync(new CommandCommitUpload(
            uploadId,
            new ValueList<ChecksumInfo>([new ChecksumInfo("c-col-1", 10, new string('a', 64))]),
            0,
            DateTime.UtcNow));

        Assert.False(commitRes.Success);
        Assert.Equal(ErrorCode.AlreadyExists, commitRes.Error);
    }

    [Fact]
    public async Task ValidateCommitUpload_RejectsInvalidChecksumFormatAndSizes()
    {
        await _store.SubmitAsync(new RegisterDataServer("ds-1", "127.0.0.1:7001", 10000, 10000, DateTime.UtcNow));

        var uploadId = "up-val-test";
        var itemId = "item-val-test";
        var planned1 = new PlannedChunk(0, "c-val-1", new ValueList<string>(["127.0.0.1:7001"]));
        var planned2 = new PlannedChunk(1, "c-val-2", new ValueList<string>(["127.0.0.1:7001"]));

        await _store.SubmitAsync(new CommandBeginUpload(
            uploadId, itemId, RootId, "val.txt", "user-1", 5000, 0, 4096,
            new ValueList<PlannedChunk>([planned1, planned2]), DateTime.UtcNow));

        // Null checksum entry
        var nullRes = await _store.SubmitAsync(new CommandCommitUpload(uploadId, new ValueList<ChecksumInfo>([null!, new ChecksumInfo("c-val-2", 904, new string('a', 64))]), 0, DateTime.UtcNow));
        Assert.False(nullRes.Success);
        Assert.Equal(ErrorCode.BadRequest, nullRes.Error);

        // Non-hex SHA-256 (uppercase or invalid chars)
        var upperSha = new string('A', 64);
        var badShaRes = await _store.SubmitAsync(new CommandCommitUpload(uploadId, new ValueList<ChecksumInfo>([
            new ChecksumInfo("c-val-1", 4096, upperSha),
            new ChecksumInfo("c-val-2", 904, new string('a', 64))
        ]), 0, DateTime.UtcNow));
        Assert.False(badShaRes.Success);
        Assert.Equal(ErrorCode.BadRequest, badShaRes.Error);

        // Size mismatch on first chunk (expected 4096, got 4000)
        var sizeMismatchRes = await _store.SubmitAsync(new CommandCommitUpload(uploadId, new ValueList<ChecksumInfo>([
            new ChecksumInfo("c-val-1", 4000, new string('a', 64)),
            new ChecksumInfo("c-val-2", 1000, new string('b', 64))
        ]), 0, DateTime.UtcNow));
        Assert.False(sizeMismatchRes.Success);
        Assert.Equal(ErrorCode.BadRequest, sizeMismatchRes.Error);

        // Duplicate chunk IDs
        var dupRes = await _store.SubmitAsync(new CommandCommitUpload(uploadId, new ValueList<ChecksumInfo>([
            new ChecksumInfo("c-val-1", 4096, new string('a', 64)),
            new ChecksumInfo("c-val-1", 904, new string('b', 64))
        ]), 0, DateTime.UtcNow));
        Assert.False(dupRes.Success);
        Assert.Equal(ErrorCode.BadRequest, dupRes.Error);
    }

    [Fact]
    public async Task CommitUpload_OutOfOrderChecksums_SucceedsAndPreservesChunkIndices()
    {
        await _store.SubmitAsync(new RegisterDataServer("ds-1", "127.0.0.1:7001", 10000, 10000, DateTime.UtcNow));

        var uploadId = "up-out-of-order";
        var itemId = "item-out-of-order";
        var planned0 = new PlannedChunk(0, "c-ooo-0", new ValueList<string>(["127.0.0.1:7001"]));
        var planned1 = new PlannedChunk(1, "c-ooo-1", new ValueList<string>(["127.0.0.1:7001"]));

        await _store.SubmitAsync(new CommandBeginUpload(
            uploadId, itemId, RootId, "ooo.txt", "user-1", 6000, 0, 4096,
            new ValueList<PlannedChunk>([planned0, planned1]), DateTime.UtcNow));

        var sha0 = Convert.ToHexStringLower(SHA256.HashData("chunk0"u8));
        var sha1 = Convert.ToHexStringLower(SHA256.HashData("chunk1"u8));

        // Submit in reverse order: chunk 1 then chunk 0
        var reverseChecksums = new ValueList<ChecksumInfo>([
            new ChecksumInfo("c-ooo-1", 1904, sha1),
            new ChecksumInfo("c-ooo-0", 4096, sha0)
        ]);

        var commitRes = await _store.SubmitAsync(new CommandCommitUpload(uploadId, reverseChecksums, 0, DateTime.UtcNow));
        Assert.True(commitRes.Success);

        var chunks = await _store.GetVersionChunksAsync(itemId, 1);
        Assert.Equal(2, chunks.Count);
        Assert.Equal("c-ooo-0", chunks[0].ChunkId);
        Assert.Equal(0, chunks[0].Index);
        Assert.Equal(4096, chunks[0].Size);
        Assert.Equal("c-ooo-1", chunks[1].ChunkId);
        Assert.Equal(1, chunks[1].Index);
        Assert.Equal(1904, chunks[1].Size);
    }
}
