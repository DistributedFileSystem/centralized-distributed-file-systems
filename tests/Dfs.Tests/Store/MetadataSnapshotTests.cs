using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Dfs.MetadataServer.Config;
using Dfs.MetadataServer.Hosting;
using Dfs.MetadataServer.Store;
using Dfs.Shared.Commands;
using Dfs.Shared.Messages;
using Dfs.Shared.Security;
using Microsoft.Data.Sqlite;
using CreateFolder = Dfs.Shared.Commands.CreateFolder;
using CreateUser = Dfs.Shared.Commands.CreateUser;
using CommandBeginUpload = Dfs.Shared.Commands.BeginUpload;
using CommandCommitUpload = Dfs.Shared.Commands.CommitUpload;

namespace Dfs.Tests.Store;

/// <summary>Tests for snapshot creation, validation, automatic snapshotting, and startup recovery in <see cref="MetadataStore"/>.</summary>
public sealed class MetadataSnapshotTests : IDisposable
{
    private const string RootId = "root";
    private static readonly DateTime T0 = new(2026, 11, 27, 10, 0, 0, DateTimeKind.Utc);

    private readonly List<string> _dirs = new();
    private readonly List<IDisposable> _stores = new();

    public void Dispose()
    {
        foreach (var s in _stores)
        {
            try { s.Dispose(); } catch { }
        }
        foreach (var d in _dirs)
        {
            try { Directory.Delete(d, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private string NewDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "dfs-snap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        _dirs.Add(d);
        return d;
    }

    private static string LogPath(string dir) => Path.Combine(dir, "oplog.jsonl");
    private static string DbPath(string dir) => Path.Combine(dir, "metadata.db");

    private MetadataStore Open(string dir, string? snapshotDir = null, int snapshotIntervalOps = 0)
    {
        var s = new MetadataStore(LogPath(dir), DbPath(dir), snapshotDir, snapshotIntervalOps);
        _stores.Add(s);
        return s;
    }

    private void Close(MetadataStore s)
    {
        s.Dispose();
        _stores.Remove(s);
    }

    private static void DeleteSqlite(string dir)
    {
        foreach (var f in Directory.GetFiles(dir, "metadata.db*"))
        {
            File.Delete(f);
        }
    }

    private static CreateFolder Folder(string id, string name, string parent = RootId, DateTime? at = null) =>
        new(id, parent, name, "u-owner", at ?? T0);

    private static CreateUser User(string id, string name, DateTime? at = null) =>
        new(id, name, "hash-" + id, "salt-" + id, at ?? T0);

    private static string Sha256Hex(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }

    private static long ReadAppliedSeq(string sqlitePath)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = sqlitePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT seq FROM applied_seq WHERE id = 1;";
        return (long)cmd.ExecuteScalar()!;
    }

    [Fact]
    public async Task TakeSnapshot_CreatesSnapshotDbAndValidMetaJson()
    {
        var dir = NewDir();
        var store = Open(dir, snapshotIntervalOps: 0);
        await store.SubmitAsync(Folder("f1", "docs"));
        await store.SubmitAsync(User("u1", "alice"));

        var seq = store.TakeSnapshot();
        Assert.Equal(2, seq);
        Assert.Equal(2, store.AppliedSeq);

        var snapDb = Path.Combine(store.SnapshotDirectory, "snapshot.db");
        var snapMeta = Path.Combine(store.SnapshotDirectory, "snapshot.meta.json");
        Assert.True(File.Exists(snapDb));
        Assert.True(File.Exists(snapMeta));
        Assert.False(File.Exists(snapDb + ".tmp"));
        Assert.False(File.Exists(snapMeta + ".tmp"));

        using var doc = JsonDocument.Parse(File.ReadAllText(snapMeta));
        Assert.Equal(2, doc.RootElement.GetProperty("lastSeq").GetInt64());
        var createdAt = doc.RootElement.GetProperty("createdAt").GetString()!;
        Assert.EndsWith("Z", createdAt);
        Assert.True(DateTimeOffset.TryParse(createdAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _));
        Assert.Equal(Sha256Hex(snapDb), doc.RootElement.GetProperty("sha256").GetString());
        Assert.Equal(2, ReadAppliedSeq(snapDb));
    }

    [Fact]
    public void TakeSnapshot_OnEmptyStore_SucceedsWithZeroLastSeq()
    {
        var dir = NewDir();
        var store = Open(dir, snapshotIntervalOps: 0);
        Assert.Equal(0, store.TakeSnapshot());
        Assert.Equal(0, ReadAppliedSeq(Path.Combine(store.SnapshotDirectory, "snapshot.db")));
    }

    [Fact]
    public void Constructor_TwoArgOverload_DefaultsSnapshotDirectoryUnderDbDir()
    {
        var dir = NewDir();
        var store = new MetadataStore(LogPath(dir), DbPath(dir));
        _stores.Add(store);
        Assert.Equal(Path.Combine(Path.GetFullPath(dir), "snapshots"), store.SnapshotDirectory);
    }

    [Fact]
    public async Task AutoSnapshot_TriggersAtConfiguredInterval()
    {
        var dir = NewDir();
        var store = Open(dir, snapshotIntervalOps: 3);
        var metaPath = Path.Combine(store.SnapshotDirectory, "snapshot.meta.json");

        await store.SubmitAsync(Folder("f1", "a"));
        await store.SubmitAsync(Folder("f2", "b"));
        Assert.False(File.Exists(metaPath));

        await store.SubmitAsync(Folder("f3", "c"));
        Assert.True(File.Exists(metaPath));
        using (var d1 = JsonDocument.Parse(File.ReadAllText(metaPath)))
            Assert.Equal(3, d1.RootElement.GetProperty("lastSeq").GetInt64());

        await store.SubmitAsync(Folder("f4", "d"));
        await store.SubmitAsync(Folder("f5", "e"));
        await store.SubmitAsync(Folder("f6", "f"));
        using (var d2 = JsonDocument.Parse(File.ReadAllText(metaPath)))
            Assert.Equal(6, d2.RootElement.GetProperty("lastSeq").GetInt64());
    }

    [Fact]
    public async Task AutoSnapshot_WithZeroOrNegativeInterval_NeverTriggersAutomatically()
    {
        var dir = NewDir();
        var store = Open(dir, snapshotIntervalOps: 0);
        for (var i = 1; i <= 5; i++) await store.SubmitAsync(Folder($"f{i}", $"d{i}"));
        Assert.False(File.Exists(Path.Combine(store.SnapshotDirectory, "snapshot.meta.json")));
    }

    [Fact]
    public async Task TakeSnapshot_CalledMultipleTimes_OverwritesCleanlyAndAdvancesLastSeq()
    {
        var dir = NewDir();
        var store = Open(dir, snapshotIntervalOps: 0);
        await store.SubmitAsync(Folder("f1", "one"));
        Assert.Equal(1, store.TakeSnapshot());

        await store.SubmitAsync(Folder("f2", "two"));
        await store.SubmitAsync(Folder("f3", "three"));
        Assert.Equal(3, store.TakeSnapshot());

        var snapDb = Path.Combine(store.SnapshotDirectory, "snapshot.db");
        var metaPath = Path.Combine(store.SnapshotDirectory, "snapshot.meta.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(metaPath));
        Assert.Equal(3, doc.RootElement.GetProperty("lastSeq").GetInt64());
        Assert.Equal(Sha256Hex(snapDb), doc.RootElement.GetProperty("sha256").GetString());
        Assert.Equal(3, ReadAppliedSeq(snapDb));
    }

    [Fact]
    public async Task Restart_AfterDeletingSqlite_LoadsSnapshotAndSkipsOpLogEntriesUpToLastSeq()
    {
        var dir = NewDir();
        var store = Open(dir, snapshotIntervalOps: 0);
        await store.SubmitAsync(Folder("f1", "docs"));
        await store.SubmitAsync(Folder("f2", "media"));
        await store.SubmitAsync(Folder("f3", "specs", "f1"));

        Assert.Equal(3, store.TakeSnapshot());

        await store.SubmitAsync(Folder("f4", "tail-a"));
        await store.SubmitAsync(Folder("f5", "tail-b", "f4"));
        Close(store);

        // Mutate seq 1 in oplog.jsonl to prove recovery loads snapshot.db instead of replaying seq <= 3.
        var lines = File.ReadAllLines(LogPath(dir));
        lines[0] = lines[0].Replace("docs", "docs-from-oplog", StringComparison.Ordinal);
        File.WriteAllLines(LogPath(dir), lines);

        DeleteSqlite(dir);

        var reopened = Open(dir, snapshotIntervalOps: 0);
        Assert.Equal(5, reopened.AppliedSeq);
        Assert.NotNull(await reopened.FindItemByPathAsync("/docs"));
        Assert.Null(await reopened.FindItemByPathAsync("/docs-from-oplog"));
        Assert.NotNull(await reopened.FindItemByPathAsync("/docs/specs"));
        Assert.NotNull(await reopened.FindItemByPathAsync("/tail-a/tail-b"));
    }

    [Fact]
    public async Task Restart_AfterDeletingSqlite_RestoresRichDomainState()
    {
        var dir = NewDir();
        var store = Open(dir, snapshotIntervalOps: 0);

        Assert.True((await store.SubmitAsync(new RegisterDataServer("ds-1", "10.0.0.1:7001", 1_000_000, 900_000, T0))).Success);
        Assert.True((await store.SubmitAsync(new UpdateHeartbeat("ds-1", 850_000, 2, T0.AddSeconds(1)))).Success);
        Assert.True((await store.SubmitAsync(new CreateUser("u-alice", "alice", "hash-a", "salt-a", T0.AddSeconds(2)))).Success);
        Assert.True((await store.SubmitAsync(new Dfs.Shared.Commands.CreateGroup("g-eng", "Engineering", T0.AddSeconds(3)))).Success);
        Assert.True((await store.SubmitAsync(new Dfs.Shared.Commands.SetGroupMember("g-eng", "u-alice", true, T0.AddSeconds(4)))).Success);
        Assert.True((await store.SubmitAsync(new Dfs.Shared.Commands.CreateRole("r-custom", "CustomEditor", RoleScope.Item, new ValueList<string>(new[] { Permission.FileRead, Permission.FileWrite }), T0.AddSeconds(5)))).Success);
        Assert.True((await store.SubmitAsync(new CreateFolder("f-proj", RootId, "projects", "u-alice", T0.AddSeconds(6)))).Success);
        Assert.True((await store.SubmitAsync(new SetGrant("gr-1", "r-custom", SubjectType.Group, "g-eng", "f-proj", T0.AddSeconds(7)))).Success);
        Assert.True((await store.SubmitAsync(new CreateSession("tok-alice", "u-alice", T0.AddHours(2), T0.AddSeconds(8)))).Success);

        var chunksV1 = new ValueList<PlannedChunk>(new[] { new PlannedChunk(0, "chk-v1-0", new ValueList<string>(new[] { "ds-1" })) });
        Assert.True((await store.SubmitAsync(new CommandBeginUpload("up-1", "file-1", "f-proj", "report.txt", "u-alice", 128, 0, 1024, chunksV1, T0.AddSeconds(9)))).Success);
        Assert.True((await store.SubmitAsync(new CommandCommitUpload("up-1", new ValueList<ChecksumInfo>(new[] { new ChecksumInfo("chk-v1-0", 128, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa") }), 5, T0.AddSeconds(10)))).Success);
        Assert.True((await store.SubmitAsync(new RecordAudit("u-alice", "CommitUpload", "/projects/report.txt", true, T0.AddSeconds(11)))).Success);

        Assert.Equal(12, store.TakeSnapshot());

        // Post-snapshot mutations: File v2 + audit
        var chunksV2 = new ValueList<PlannedChunk>(new[] { new PlannedChunk(0, "chk-v2-0", new ValueList<string>(new[] { "ds-1" })) });
        Assert.True((await store.SubmitAsync(new CommandBeginUpload("up-2", "file-1", "f-proj", "report.txt", "u-alice", 256, 1, 1024, chunksV2, T0.AddSeconds(12)))).Success);
        Assert.True((await store.SubmitAsync(new CommandCommitUpload("up-2", new ValueList<ChecksumInfo>(new[] { new ChecksumInfo("chk-v2-0", 256, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb") }), 5, T0.AddSeconds(13)))).Success);
        Assert.True((await store.SubmitAsync(new RecordAudit("u-alice", "CommitUpload", "/projects/report.txt#v2", true, T0.AddSeconds(14)))).Success);

        Close(store);
        DeleteSqlite(dir);

        var reopened = Open(dir, snapshotIntervalOps: 0);
        Assert.Equal(15, reopened.AppliedSeq);

        var ds = await reopened.FindDataServerByIdAsync("ds-1");
        Assert.NotNull(ds);
        Assert.Equal(850_000 - 128 - 256, ds!.FreeBytes);

        Assert.NotNull(await reopened.FindUserByNameAsync("alice"));
        Assert.Single(await reopened.ListGroupsAsync());
        Assert.Contains(await reopened.ListRolesAsync(), r => r.Id == "r-custom");
        Assert.Contains(await reopened.ListGrantsAsync("f-proj", "g-eng", false), g => g.Id == "gr-1");
        Assert.Equal("u-alice", (await reopened.FindSessionAsync("tok-alice"))?.UserId);

        var file = await reopened.FindItemByPathAsync("/projects/report.txt");
        Assert.NotNull(file);
        Assert.Equal(2, file!.CurrentVersion);
        Assert.Equal(256, file.Size);
        Assert.Equal(2, (await reopened.ListVersionsAsync("file-1")).Count);
        Assert.Equal("chk-v2-0", Assert.Single(await reopened.GetVersionChunksAsync("file-1", 2)).ChunkId);
        Assert.Equal(2, (await reopened.ListAuditAsync(null, null, "u-alice", 10)).Count);
    }

    [Fact]
    public async Task Restart_WithIntactSqliteAheadOfSnapshot_PreservesStateWithoutReverting()
    {
        var dir = NewDir();
        var store = Open(dir, snapshotIntervalOps: 0);
        await store.SubmitAsync(Folder("f1", "one"));
        await store.SubmitAsync(Folder("f2", "two"));
        store.TakeSnapshot();
        await store.SubmitAsync(Folder("f3", "three"));
        await store.SubmitAsync(Folder("f4", "four"));
        Close(store);

        var reopened = Open(dir, snapshotIntervalOps: 0);
        Assert.Equal(4, reopened.AppliedSeq);
        Assert.NotNull(await reopened.FindItemByPathAsync("/four"));
        var next = await reopened.SubmitAsync(Folder("f5", "five"));
        Assert.Equal(5, next.Seq);
    }

    [Fact]
    public async Task Restart_WithStaleSqliteOlderThanSnapshot_RestoresNewerSnapshotAndReplaysTail()
    {
        var dir = NewDir();
        var store = Open(dir, snapshotIntervalOps: 0);
        await store.SubmitAsync(Folder("f1", "one"));
        Close(store);

        var staleCopy = Path.Combine(dir, "stale.db");
        File.Copy(DbPath(dir), staleCopy);

        store = Open(dir, snapshotIntervalOps: 0);
        await store.SubmitAsync(Folder("f2", "two"));
        await store.SubmitAsync(Folder("f3", "three"));
        store.TakeSnapshot();
        await store.SubmitAsync(Folder("f4", "four"));
        Close(store);

        DeleteSqlite(dir);
        File.Copy(staleCopy, DbPath(dir));

        var reopened = Open(dir, snapshotIntervalOps: 0);
        Assert.Equal(4, reopened.AppliedSeq);
        Assert.NotNull(await reopened.FindItemByPathAsync("/three"));
        Assert.NotNull(await reopened.FindItemByPathAsync("/four"));
    }

    [Fact]
    public async Task Restart_WithCorruptSqliteAndValidSnapshot_RestoresFromSnapshot()
    {
        var dir = NewDir();
        var store = Open(dir, snapshotIntervalOps: 0);
        await store.SubmitAsync(Folder("f1", "one"));
        store.TakeSnapshot();
        await store.SubmitAsync(Folder("f2", "two"));
        Close(store);

        File.WriteAllText(DbPath(dir), "not-a-valid-sqlite-database");

        var reopened = Open(dir, snapshotIntervalOps: 0);
        Assert.Equal(2, reopened.AppliedSeq);
        Assert.NotNull(await reopened.FindItemByPathAsync("/one"));
        Assert.NotNull(await reopened.FindItemByPathAsync("/two"));
    }

    [Fact]
    public async Task Restart_WithCorruptSnapshotDbSha256Mismatch_FallsBackToFullOpLogReplay()
    {
        var dir = NewDir();
        var store = Open(dir, snapshotIntervalOps: 0);
        await store.SubmitAsync(Folder("f1", "one"));
        await store.SubmitAsync(Folder("f2", "two"));
        store.TakeSnapshot();
        var snapDb = Path.Combine(store.SnapshotDirectory, "snapshot.db");
        Close(store);

        File.WriteAllText(snapDb, "corrupted-snapshot-bytes");
        DeleteSqlite(dir);

        var reopened = Open(dir, snapshotIntervalOps: 0);
        Assert.Equal(2, reopened.AppliedSeq);
        Assert.NotNull(await reopened.FindItemByPathAsync("/one"));
        Assert.NotNull(await reopened.FindItemByPathAsync("/two"));
    }

    [Fact]
    public async Task Restart_WithCorruptSnapshotMetaJson_FallsBackToFullOpLogReplay()
    {
        var dir = NewDir();
        var store = Open(dir, snapshotIntervalOps: 0);
        await store.SubmitAsync(Folder("f1", "one"));
        store.TakeSnapshot();
        var metaPath = Path.Combine(store.SnapshotDirectory, "snapshot.meta.json");
        Close(store);

        File.WriteAllText(metaPath, "{ invalid json");
        DeleteSqlite(dir);

        var reopened = Open(dir, snapshotIntervalOps: 0);
        Assert.Equal(1, reopened.AppliedSeq);
        Assert.NotNull(await reopened.FindItemByPathAsync("/one"));
    }

    [Fact]
    public async Task Restart_WithSnapshotLastSeqMismatchingDb_FallsBackToFullOpLogReplay()
    {
        var dir = NewDir();
        var store = Open(dir, snapshotIntervalOps: 0);
        await store.SubmitAsync(Folder("f1", "one"));
        await store.SubmitAsync(Folder("f2", "two"));
        store.TakeSnapshot();
        var snapDb = Path.Combine(store.SnapshotDirectory, "snapshot.db");
        var metaPath = Path.Combine(store.SnapshotDirectory, "snapshot.meta.json");
        Close(store);

        File.WriteAllText(metaPath, JsonSerializer.Serialize(new { lastSeq = 1, createdAt = "2026-11-27T10:00:00Z", sha256 = Sha256Hex(snapDb) }));
        DeleteSqlite(dir);

        var reopened = Open(dir, snapshotIntervalOps: 0);
        Assert.Equal(2, reopened.AppliedSeq);
        Assert.NotNull(await reopened.FindItemByPathAsync("/two"));
    }

    [Fact]
    public async Task Restart_WithSnapshotLastSeqAheadOfOpLog_FallsBackToFullOpLogReplay()
    {
        var dir = NewDir();
        var store = Open(dir, snapshotIntervalOps: 0);
        await store.SubmitAsync(Folder("f1", "one"));
        await store.SubmitAsync(Folder("f2", "two"));
        store.TakeSnapshot(); // lastSeq = 2
        Close(store);

        // Truncate oplog.jsonl to 1 entry so snapshot.lastSeq (2) > opLog.LastSeq (1)
        File.WriteAllLines(LogPath(dir), File.ReadAllLines(LogPath(dir)).Take(1));
        DeleteSqlite(dir);

        var reopened = Open(dir, snapshotIntervalOps: 0);
        Assert.Equal(1, reopened.AppliedSeq);
        Assert.NotNull(await reopened.FindItemByPathAsync("/one"));
        Assert.Null(await reopened.FindItemByPathAsync("/two"));
    }

    [Fact]
    public async Task Restart_WhenSqliteAppliedSeqAheadOfOpLog_RebuildsFromSnapshotAndOpLog()
    {
        var dir = NewDir();
        var store = Open(dir, snapshotIntervalOps: 0);
        await store.SubmitAsync(Folder("f1", "one"));
        await store.SubmitAsync(Folder("f2", "two"));
        store.TakeSnapshot(); // lastSeq = 2
        await store.SubmitAsync(Folder("f3", "three"));
        Close(store);

        // Truncate oplog.jsonl back to 2 entries so metadata.db (applied_seq = 3) > opLog.LastSeq (2).
        File.WriteAllLines(LogPath(dir), File.ReadAllLines(LogPath(dir)).Take(2));

        var reopened = Open(dir, snapshotIntervalOps: 0);
        Assert.Equal(2, reopened.AppliedSeq);
        Assert.NotNull(await reopened.FindItemByPathAsync("/one"));
        Assert.NotNull(await reopened.FindItemByPathAsync("/two"));
        Assert.Null(await reopened.FindItemByPathAsync("/three"));

        var next = await reopened.SubmitAsync(Folder("f4", "four"));
        Assert.True(next.Success);
        Assert.Equal(3, next.Seq);
    }

    [Fact]
    public async Task Restart_AndTakeSnapshot_ResilientToLeftoverTmpFiles()
    {
        var dir = NewDir();
        var store = Open(dir, snapshotIntervalOps: 0);
        await store.SubmitAsync(Folder("f1", "alpha"));
        store.TakeSnapshot();
        await store.SubmitAsync(Folder("f2", "beta"));
        var snapDir = store.SnapshotDirectory;
        Close(store);

        File.WriteAllText(Path.Combine(snapDir, "snapshot.db.tmp"), "corrupt-tmp-db");
        File.WriteAllText(Path.Combine(snapDir, "snapshot.meta.json.tmp"), "{broken");
        File.WriteAllText(DbPath(dir) + ".restore.tmp", "corrupt-restore");
        DeleteSqlite(dir);

        var reopened = Open(dir, snapshotIntervalOps: 0);
        Assert.Equal(2, reopened.AppliedSeq);
        Assert.NotNull(await reopened.FindItemByPathAsync("/alpha"));
        Assert.NotNull(await reopened.FindItemByPathAsync("/beta"));
        Assert.False(File.Exists(DbPath(dir) + ".restore.tmp"));

        reopened.TakeSnapshot();
        Assert.False(File.Exists(Path.Combine(snapDir, "snapshot.db.tmp")));
        Assert.False(File.Exists(Path.Combine(snapDir, "snapshot.meta.json.tmp")));
    }

    [Fact]
    public async Task AutoSnapshot_UnderConcurrentWrites_ProducesConsistentRecoverableSnapshot()
    {
        var dir = NewDir();
        var store = Open(dir, snapshotIntervalOps: 5);

        var tasks = Enumerable.Range(1, 25).Select(i =>
            Task.Run(() => store.SubmitAsync(Folder($"f-{i}", $"dir-{i}", RootId, T0.AddSeconds(i)))));

        var results = await Task.WhenAll(tasks);
        Assert.All(results, r => Assert.True(r.Success));
        Assert.Equal(Enumerable.Range(1, 25).Select(i => (long)i), results.Select(r => r.Seq).OrderBy(s => s));
        Assert.Equal(25, store.AppliedSeq);

        Close(store);
        DeleteSqlite(dir);

        var reopened = Open(dir, snapshotIntervalOps: 5);
        Assert.Equal(25, reopened.AppliedSeq);
        Assert.Equal(25, (await reopened.ListChildrenAsync(RootId)).Count);
    }

    [Fact]
    public async Task TakeSnapshot_NeverTruncatesOpLog_AndSubsequentWritesContinueSeq()
    {
        var dir = NewDir();
        var store = Open(dir, snapshotIntervalOps: 2);
        await store.SubmitAsync(Folder("f1", "one"));
        await store.SubmitAsync(Folder("f2", "two"));
        Assert.Equal(2, File.ReadAllLines(LogPath(dir)).Length);

        store.TakeSnapshot();
        Assert.Equal(2, File.ReadAllLines(LogPath(dir)).Length);
        Close(store);

        DeleteSqlite(dir);
        var reopened = Open(dir, snapshotIntervalOps: 2);
        var res = await reopened.SubmitAsync(Folder("f3", "three"));
        Assert.True(res.Success);
        Assert.Equal(3, res.Seq);
        Assert.Equal(3, reopened.AppliedSeq);
        Assert.Equal(3, File.ReadAllLines(LogPath(dir)).Length);
    }

    [Fact]
    public async Task MetadataServerHost_DisposeAsync_TakesBestEffortSnapshotAndIsIdempotent()
    {
        var dir = NewDir();
        var config = new MetadataServerConfig(
            NodeRole: "metadata",
            NodeId: "m-test",
            ListenPort: 0,
            AdvertiseHost: "127.0.0.1",
            MetadataEndpoints: new[] { "127.0.0.1:7000" },
            DataDir: dir,
            TokenKey: "test-token-key-1234567890",
            AdminUser: "admin",
            AdminPassword: "secret-password",
            KeepVersions: 5,
            SnapshotIntervalOps: 0);

        var host = new MetadataServerHost(config);
        await host.StartAsync();
        await host.DisposeAsync();
        await host.DisposeAsync();

        Assert.True(File.Exists(Path.Combine(config.SnapshotDir, "snapshot.db")));
        Assert.True(File.Exists(Path.Combine(config.SnapshotDir, "snapshot.meta.json")));
    }
}
