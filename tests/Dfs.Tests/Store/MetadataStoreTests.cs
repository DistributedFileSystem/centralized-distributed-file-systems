using System.Text.Json;
using Dfs.MetadataServer.Store;
using Dfs.Shared.Commands;
using Dfs.Shared.Messages;
using Dfs.Shared.Metadata;
using CreateFolder = Dfs.Shared.Commands.CreateFolder;
using CreateUser = Dfs.Shared.Commands.CreateUser;

namespace Dfs.Tests.Store;

/// <summary>Tests for the stage 1 <see cref="IMetadataStore"/> (op log + SQLite) in Dfs.MetadataServer.</summary>
public sealed class MetadataStoreTests : IDisposable
{
    // ASSUMPTION (see test-report.md Questions): the docs do not define the root folder id; "root" is used as ParentId of top-level folders.
    private const string RootId = "root";
    private static readonly DateTime T0 = new(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc);

    private readonly List<string> _dirs = new();
    private readonly List<IDisposable> _stores = new();

    public void Dispose()
    {
        foreach (var s in _stores) s.Dispose();
        foreach (var d in _dirs)
        {
            try { Directory.Delete(d, true); } catch (IOException) { }
        }
    }

    private string NewDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "dfs-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        _dirs.Add(d);
        return d;
    }

    private static string LogPath(string dir) => Path.Combine(dir, "oplog.jsonl");
    private static string DbPath(string dir) => Path.Combine(dir, "metadata.db");

    private MetadataStore Open(string dir)
    {
        var s = new MetadataStore(LogPath(dir), DbPath(dir));
        _stores.Add(s);
        return s;
    }

    private void Close(MetadataStore s)
    {
        s.Dispose();
        _stores.Remove(s);
    }

    private static CreateFolder Folder(string id, string name, string parent = RootId, DateTime? at = null) =>
        new(id, parent, name, "u-owner", at ?? T0);

    private static CreateUser User(string id, string name, DateTime? at = null) =>
        new(id, name, "hash-" + id, "salt-" + id, at ?? T0);

    private static string[] ReadLogLines(string dir) =>
        File.ReadAllLines(LogPath(dir)).Where(l => l.Trim().Length > 0).ToArray();

    private sealed record UnsupportedCommand(DateTime At) : IMetadataCommand;

    // ---------- TC1 (AC1) ----------

    [Fact]
    public async Task SubmitAsync_CreateFolder_WritesOneOpLogLineWithSeq1TypeAtAndData()
    {
        // docs/data-model.md – Op log: {"seq":n,"type":"<Name>","at":"<UTC>","data":{…}}
        var dir = NewDir();
        var store = Open(dir);

        var result = await store.SubmitAsync(Folder("f1", "docs"));

        Assert.True(result.Success);
        Assert.Equal(1, result.Seq);

        // Read through a second handle while the store is still open: the line must already be on disk.
        using var fs = new FileStream(LogPath(dir), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(fs);
        var lines = (await reader.ReadToEndAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var line = Assert.Single(lines);

        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("seq").GetInt64());
        Assert.Equal("CreateFolder", root.GetProperty("type").GetString());
        Assert.Equal("2026-11-07T10:00:00Z", root.GetProperty("at").GetString());
        var data = root.GetProperty("data");
        Assert.Equal("f1", data.GetProperty("itemId").GetString());
        Assert.Equal(RootId, data.GetProperty("parentId").GetString());
        Assert.Equal("docs", data.GetProperty("name").GetString());
        Assert.Equal("u-owner", data.GetProperty("ownerId").GetString());
    }

    [Fact]
    public async Task SubmitAsync_CreateUser_WritesLineWithTypeCreateUser()
    {
        // docs/data-model.md – Op log
        var dir = NewDir();
        var store = Open(dir);

        var result = await store.SubmitAsync(User("u1", "alice"));

        Assert.True(result.Success);
        using var doc = JsonDocument.Parse(Assert.Single(ReadLogLines(dir)));
        Assert.Equal("CreateUser", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("alice", doc.RootElement.GetProperty("data").GetProperty("username").GetString());
    }

    // ---------- TC2 (AC1) ----------

    [Fact]
    public async Task SubmitAsync_AcceptedCommands_SeqIncreasesByOneEach()
    {
        // docs/data-model.md – Op log (seq); CommandResult.Seq
        var store = Open(NewDir());

        var r1 = await store.SubmitAsync(Folder("f1", "a"));
        var r2 = await store.SubmitAsync(User("u1", "alice"));
        var r3 = await store.SubmitAsync(Folder("f2", "b"));

        Assert.Equal(new long[] { 1, 2, 3 }, new[] { r1.Seq, r2.Seq, r3.Seq });
    }

    [Fact]
    public async Task SubmitAsync_Success_ItemVisibleThroughReadsAfterLogLine()
    {
        // docs/architecture.md – Design rules 1–3: the log line is written, then SQLite is applied
        var dir = NewDir();
        var store = Open(dir);

        await store.SubmitAsync(Folder("f1", "docs"));

        Assert.Single(ReadLogLines(dir));
        var item = await store.FindItemByIdAsync("f1");
        Assert.NotNull(item);
        Assert.Equal("docs", item!.Name);
        Assert.Equal(ItemKind.Folder, item.Kind);
        Assert.Equal("u-owner", item.OwnerId);
    }

    // ---------- TC3 (AC2) ----------

    [Fact]
    public async Task SubmitAsync_SameCommandsOnTwoFreshStores_ProduceIdenticalRowsAndLog()
    {
        // docs/architecture.md – Design rules (deterministic); docs/adr/0003 – ids and timestamps decided before logging
        var dirA = NewDir();
        var dirB = NewDir();
        var a = Open(dirA);
        var b = Open(dirB);
        var at = new DateTime(2026, 11, 7, 11, 22, 33, DateTimeKind.Utc);
        var commands = new IMetadataCommand[]
        {
            Folder("f1", "docs", RootId, at),
            Folder("f2", "reports", "f1", at.AddSeconds(5)),
            User("u1", "alice", at.AddSeconds(9)),
        };

        foreach (var c in commands)
        {
            await a.SubmitAsync(c);
            await b.SubmitAsync(c);
        }

        foreach (var id in new[] { "f1", "f2" })
        {
            var ia = await a.FindItemByIdAsync(id);
            var ib = await b.FindItemByIdAsync(id);
            Assert.NotNull(ia);
            Assert.Equal(ia, ib);
        }
        var f2 = await a.FindItemByIdAsync("f2");
        Assert.Equal("/docs/reports", f2!.Path);
        Assert.Equal(at.AddSeconds(5), f2.ModifiedAt); // timestamp = command.At, not the clock
        Assert.Equal(await a.FindUserByNameAsync("alice"), await b.FindUserByNameAsync("alice"));
        Assert.Equal(File.ReadAllText(LogPath(dirA)), File.ReadAllText(LogPath(dirB)));
    }

    [Fact]
    public async Task SubmitAsync_CreateUser_StoresCommandFieldsVerbatim()
    {
        // docs/data-model.md – USERS; Dfs.Shared CreateUser carries the precomputed hash and salt
        var store = Open(NewDir());

        await store.SubmitAsync(User("u1", "alice"));

        var byName = await store.FindUserByNameAsync("alice");
        var byId = await store.FindUserByIdAsync("u1");
        Assert.Equal(new UserCredentials("u1", "alice", "hash-u1", "salt-u1", false), byName);
        Assert.Equal(byName, byId);
        var listed = Assert.Single(await store.ListUsersAsync());
        Assert.Equal("alice", listed.Username);
    }

    // ---------- TC4 (AC3) and TC6 ----------

    private static async Task<string> DumpAsync(IMetadataStore s)
    {
        var sb = new System.Text.StringBuilder();
        async Task Walk(string folderId)
        {
            foreach (var child in (await s.ListChildrenAsync(folderId)).OrderBy(c => c.Id, StringComparer.Ordinal))
            {
                sb.Append(JsonSerializer.Serialize(child)).Append('\n');
                if (child.Kind == ItemKind.Folder) await Walk(child.Id);
            }
        }
        await Walk(RootId);
        foreach (var u in (await s.ListUsersAsync()).OrderBy(u => u.Id, StringComparer.Ordinal))
        {
            var cred = await s.FindUserByIdAsync(u.Id);
            sb.Append(JsonSerializer.Serialize(u)).Append(JsonSerializer.Serialize(cred)).Append('\n');
        }
        return sb.ToString();
    }

    [Fact]
    public async Task Open_ReplayOfOpLogIntoNewSqlite_YieldsIdenticalState()
    {
        // docs/architecture.md – Design rule 4: op log is the source of truth
        var dir = NewDir();
        var original = Open(dir);
        await original.SubmitAsync(Folder("f1", "docs"));
        await original.SubmitAsync(Folder("f2", "reports", "f1", T0.AddMinutes(1)));
        await original.SubmitAsync(User("u1", "alice", T0.AddMinutes(2)));
        var expected = await DumpAsync(original);
        Assert.False(string.IsNullOrEmpty(expected));

        var replayDir = NewDir();
        File.Copy(LogPath(dir), LogPath(replayDir));
        var replayed = Open(replayDir);

        Assert.Equal(expected, await DumpAsync(replayed));
    }

    [Fact]
    public async Task Open_SqliteFileDeleted_RebuildsFromOpLog()
    {
        // docs/data-model.md – Op log; docs/adr/0003 – "xoá SQLite → dựng lại từ log"
        var dir = NewDir();
        var store = Open(dir);
        await store.SubmitAsync(Folder("f1", "docs"));
        await store.SubmitAsync(Folder("f2", "reports", "f1"));
        Close(store);
        foreach (var f in Directory.GetFiles(dir, "metadata.db*")) File.Delete(f);

        var reopened = Open(dir);

        var found = await reopened.FindItemByPathAsync("/docs/reports");
        Assert.NotNull(found);
        Assert.Equal("f2", found!.Id);
        var next = await reopened.SubmitAsync(Folder("f3", "other"));
        Assert.Equal(3, next.Seq);
    }

    // ---------- TC5 ----------

    [Fact]
    public async Task SubmitAsync_DuplicateFolderNameInSameParent_ReturnsAlreadyExistsAndNotLogged()
    {
        // Dfs.Shared ErrorCode.AlreadyExists; rejected commands are not written to the op log
        var dir = NewDir();
        var store = Open(dir);
        await store.SubmitAsync(Folder("f1", "docs"));

        var dup = await store.SubmitAsync(Folder("f2", "docs"));

        Assert.False(dup.Success);
        Assert.Equal(ErrorCode.AlreadyExists, dup.Error);
        Assert.Equal(0, dup.Seq);
        Assert.Single(ReadLogLines(dir));
        Assert.Null(await store.FindItemByIdAsync("f2"));
    }

    [Fact]
    public async Task SubmitAsync_SameNameInDifferentParents_Succeeds()
    {
        // uniqueness is per folder
        var store = Open(NewDir());
        await store.SubmitAsync(Folder("f1", "a"));

        var r = await store.SubmitAsync(Folder("f2", "a", "f1"));

        Assert.True(r.Success);
    }

    [Fact]
    public async Task SubmitAsync_UnknownParent_ReturnsNotFoundAndNotLogged()
    {
        // Dfs.Shared ErrorCode.NotFound
        var dir = NewDir();
        var store = Open(dir);

        var r = await store.SubmitAsync(Folder("f1", "docs", "missing-parent"));

        Assert.False(r.Success);
        Assert.Equal(ErrorCode.NotFound, r.Error);
        Assert.Equal(0, r.Seq);
        Assert.False(File.Exists(LogPath(dir)) && ReadLogLines(dir).Length > 0);
    }

    [Fact]
    public async Task SubmitAsync_DuplicateUsername_ReturnsAlreadyExistsAndNotLogged()
    {
        // Dfs.Shared ErrorCode.AlreadyExists
        var dir = NewDir();
        var store = Open(dir);
        await store.SubmitAsync(User("u1", "alice"));

        var r = await store.SubmitAsync(User("u2", "alice"));

        Assert.False(r.Success);
        Assert.Equal(ErrorCode.AlreadyExists, r.Error);
        Assert.Single(ReadLogLines(dir));
        Assert.Null(await store.FindUserByIdAsync("u2"));
    }

    [Fact]
    public async Task SubmitAsync_AfterRejectedCommand_SeqIsNotConsumed()
    {
        // rejected commands do not consume a seq
        var store = Open(NewDir());
        var first = await store.SubmitAsync(Folder("f1", "docs"));
        await store.SubmitAsync(Folder("f2", "docs"));          // rejected
        await store.SubmitAsync(Folder("f3", "x", "missing"));  // rejected

        var next = await store.SubmitAsync(Folder("f4", "other"));

        Assert.Equal(1, first.Seq);
        Assert.Equal(2, next.Seq);
    }

    // ---------- TC7 ----------

    [Fact]
    public async Task Open_SqliteBehindLogTail_ReplaysOnlyLinesAfterAppliedSeq()
    {
        // docs/data-model.md – start-up replays lines with seq > last applied seq
        var dir = NewDir();
        var store = Open(dir);
        await store.SubmitAsync(Folder("f1", "docs"));
        await store.SubmitAsync(Folder("f2", "reports", "f1"));
        Close(store);

        // A line that reached the log (fsync) but was not applied before a crash.
        var extra = "{\"seq\":3,\"type\":\"CreateFolder\",\"at\":\"2026-11-07T10:05:00Z\",\"data\":{\"itemId\":\"f3\",\"parentId\":\"root\",\"name\":\"late\",\"ownerId\":\"u-owner\"}}\n";
        File.AppendAllText(LogPath(dir), extra);

        // Replaying seq 1–2 again would be rejected as duplicates and break start-up, so success proves they are skipped.
        var reopened = Open(dir);

        var late = await reopened.FindItemByIdAsync("f3");
        Assert.NotNull(late);
        Assert.Equal("late", late!.Name);
        Assert.Equal(new DateTime(2026, 11, 7, 10, 5, 0, DateTimeKind.Utc), late.ModifiedAt);
        Assert.Equal("f2", Assert.Single(await reopened.ListChildrenAsync("f1")).Id);
        var next = await reopened.SubmitAsync(Folder("f4", "after"));
        Assert.Equal(4, next.Seq);
    }

    // ---------- TC8 ----------

    [Fact]
    public async Task SubmitAsync_FiftyConcurrentCommands_GetUniqueGaplessSeqAndFiftyLines()
    {
        // docs/architecture.md – Design rule 2: one write path; seq has no gaps
        var dir = NewDir();
        var store = Open(dir);

        var results = await Task.WhenAll(Enumerable.Range(0, 50)
            .Select(i => Task.Run(() => store.SubmitAsync(Folder("f" + i, "n" + i)))));

        Assert.All(results, r => Assert.True(r.Success));
        Assert.Equal(Enumerable.Range(1, 50).Select(i => (long)i), results.Select(r => r.Seq).OrderBy(s => s));
        var seqs = ReadLogLines(dir).Select(l => JsonDocument.Parse(l).RootElement.GetProperty("seq").GetInt64()).ToArray();
        Assert.Equal(Enumerable.Range(1, 50).Select(i => (long)i), seqs); // file order == seq order
    }

    // ---------- TC9 ----------

    [Fact]
    public async Task SubmitAsync_UnsupportedCommandType_ReturnsBadRequestAndLogUnchanged()
    {
        // Dfs.Shared ErrorCode.BadRequest; rejected commands are returned, not thrown
        var dir = NewDir();
        var store = Open(dir);
        await store.SubmitAsync(Folder("f1", "docs"));
        var before = File.ReadAllText(LogPath(dir));

        var r = await store.SubmitAsync(new UnsupportedCommand(T0));

        Assert.False(r.Success);
        Assert.Equal(ErrorCode.BadRequest, r.Error);
        Assert.Equal(before, File.ReadAllText(LogPath(dir)));
        var next = await store.SubmitAsync(Folder("f2", "b"));
        Assert.Equal(2, next.Seq);
    }

    // ---------- TC10 ----------

    [Theory]
    [InlineData("{\"seq\":3,\"type\":\"Create")]  // torn write, no newline
    [InlineData("\n\n")]                          // blank trailing lines
    [InlineData("   \n")]                         // whitespace-only line
    public async Task Open_TornOrBlankLastLine_IsIgnoredAndNextAppendContinuesAtLastValidSeqPlusOne(string tail)
    {
        // plan.md TC10: crash while appending must not prevent start-up
        var dir = NewDir();
        var store = Open(dir);
        await store.SubmitAsync(Folder("f1", "docs"));
        await store.SubmitAsync(Folder("f2", "reports", "f1"));
        Close(store);
        File.AppendAllText(LogPath(dir), tail);

        var reopened = Open(dir);
        var r = await reopened.SubmitAsync(Folder("f3", "next"));

        Assert.True(r.Success);
        Assert.Equal(3, r.Seq);
        Assert.NotNull(await reopened.FindItemByPathAsync("/docs/reports"));
        Assert.NotNull(await reopened.FindItemByIdAsync("f3"));

        // The newly appended line must be a complete JSON line with seq 3.
        var last = File.ReadAllLines(LogPath(dir)).Last(l => l.Trim().Length > 0);
        using var doc = JsonDocument.Parse(last);
        Assert.Equal(3, doc.RootElement.GetProperty("seq").GetInt64());
        Assert.Equal("f3", doc.RootElement.GetProperty("data").GetProperty("itemId").GetString());

        // And a further restart sees all three.
        Close(reopened);
        var again = Open(dir);
        Assert.NotNull(await again.FindItemByIdAsync("f3"));
    }

    // ---------- reads that have no table yet ----------

    [Fact]
    public async Task Reads_WithoutBackingTables_ReturnEmptyOrNull()
    {
        // plan.md "Files to touch": versions, uploads, grants… return empty/null in this task
        var store = Open(NewDir());

        Assert.Null(await store.FindItemByPathAsync("/nope"));
        Assert.Null(await store.FindItemByIdAsync("nope"));
        Assert.Null(await store.FindUserByNameAsync("nobody"));
        Assert.Null(await store.FindUploadAsync("up1"));
        Assert.Null(await store.FindSessionAsync("tok"));
        Assert.Empty(await store.ListVersionsAsync("f1"));
        Assert.Empty(await store.GetVersionChunksAsync("f1", 1));
        Assert.Empty(await store.ListGrantsAsync(null, null, false));
    }
}
