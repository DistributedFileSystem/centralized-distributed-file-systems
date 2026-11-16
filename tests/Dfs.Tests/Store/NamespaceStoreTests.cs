using Dfs.MetadataServer.Store;
using Dfs.Shared.Commands;
using Dfs.Shared.Messages;
using Dfs.Shared.Metadata;
using Microsoft.Data.Sqlite;
using CreateFolder = Dfs.Shared.Commands.CreateFolder;
using DeleteItem = Dfs.Shared.Commands.DeleteItem;

namespace Dfs.Tests.Store;

/// <summary>Unit tests for deterministic namespace mutations (<see cref="RenameItem"/>, <see cref="MoveItem"/>, <see cref="DeleteItem"/>) in <see cref="MetadataStore"/>.</summary>
public sealed class NamespaceStoreTests : IDisposable
{
    private const string RootId = "root";
    private static readonly DateTime T0 = new(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);

    private readonly List<string> _dirs = new();
    private readonly List<IDisposable> _stores = new();

    public void Dispose()
    {
        foreach (var store in _stores)
        {
            store.Dispose();
        }

        foreach (var dir in _dirs)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
            catch (IOException)
            {
            }
        }
    }

    private string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dfs-ns-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _dirs.Add(dir);
        return dir;
    }

    private static string LogPath(string dir) => Path.Combine(dir, "oplog.jsonl");
    private static string DbPath(string dir) => Path.Combine(dir, "metadata.db");

    private MetadataStore Open(string dir)
    {
        var store = new MetadataStore(LogPath(dir), DbPath(dir));
        _stores.Add(store);
        return store;
    }

    private void Close(MetadataStore store)
    {
        store.Dispose();
        _stores.Remove(store);
    }

    private static CreateFolder Folder(string id, string name, string parent = RootId, DateTime? at = null) =>
        new(id, parent, name, "u-owner", at ?? T0);

    private static string[] ReadLogLines(string dir) =>
        File.ReadAllLines(LogPath(dir)).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();

    private static void SeedFileWithVersionAndGrant(string dir, string id, string parentId, string name)
    {
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbPath(dir), Pooling = false }.ToString());
        conn.Open();
        MetadataSchema.Initialize(conn);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO roles (id, name, scope) VALUES ('r-ed', 'EditorTest', 'Item');
            INSERT INTO chunks (id, size, sha256) VALUES ('c-1', 12, 'abc');
            INSERT INTO items (id, parent_id, name, kind, owner_id, current_version, size, modified_at)
              VALUES ($id, $p, $n, 'File', 'u-owner', 1, 12, '2026-11-10T09:00:00Z');
            INSERT INTO file_versions (item_id, version, size, created_by, created_at)
              VALUES ($id, 1, 12, 'u-owner', '2026-11-10T09:00:00Z');
            INSERT INTO version_chunks (item_id, version, idx, chunk_id)
              VALUES ($id, 1, 0, 'c-1');
            INSERT INTO grants (id, role_id, subject_type, subject_id, item_id)
              VALUES ('g-1', 'r-ed', 'User', 'u-owner', $id);
            INSERT INTO uploads (id, item_id, base_version, size, chunk_size, created_by, created_at)
              VALUES ('up-1', $id, 1, 12, 4194304, 'u-owner', '2026-11-10T09:00:00Z');
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$p", parentId);
        cmd.Parameters.AddWithValue("$n", name);
        cmd.ExecuteNonQuery();
    }

    private static long CountRows(string dir, string table, string whereColumn, string value)
    {
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbPath(dir), Pooling = false }.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table} WHERE {whereColumn} = $v";
        cmd.Parameters.AddWithValue("$v", value);
        return (long)cmd.ExecuteScalar()!;
    }

    [Fact]
    public async Task RenameItem_UpdatesFolderAndDescendantPaths_AndWritesOpLog()
    {
        var dir = NewDir();
        var store = Open(dir);

        Assert.True((await store.SubmitAsync(Folder("f1", "docs"))).Success);
        Assert.True((await store.SubmitAsync(Folder("f2", "reports", "f1"))).Success);

        var t1 = T0.AddMinutes(5);
        var res = await store.SubmitAsync(new RenameItem("f1", "archive", t1));
        Assert.True(res.Success);
        Assert.Equal(3, res.Seq);

        Assert.Null(await store.FindItemByPathAsync("/docs"));
        Assert.Null(await store.FindItemByPathAsync("/docs/reports"));

        var renamed = await store.FindItemByPathAsync("/archive");
        Assert.NotNull(renamed);
        Assert.Equal("archive", renamed.Name);
        Assert.Equal(t1, renamed.ModifiedAt);

        var child = await store.FindItemByPathAsync("/archive/reports");
        Assert.NotNull(child);
        Assert.Equal("f2", child.Id);
        Assert.Equal(3, ReadLogLines(dir).Length);
    }

    [Fact]
    public async Task RenameItem_RejectsDuplicateInvalidAndMissingCases_WithoutLogging()
    {
        var dir = NewDir();
        var store = Open(dir);

        await store.SubmitAsync(Folder("f1", "docs"));
        await store.SubmitAsync(Folder("f2", "media"));
        var linesBefore = ReadLogLines(dir).Length;

        var dup = await store.SubmitAsync(new RenameItem("f1", "media", T0.AddMinutes(1)));
        Assert.False(dup.Success);
        Assert.Equal(ErrorCode.AlreadyExists, dup.Error);

        var missing = await store.SubmitAsync(new RenameItem("missing", "other", T0.AddMinutes(1)));
        Assert.False(missing.Success);
        Assert.Equal(ErrorCode.NotFound, missing.Error);

        var badRoot = await store.SubmitAsync(new RenameItem(RootId, "other", T0.AddMinutes(1)));
        Assert.False(badRoot.Success);
        Assert.Equal(ErrorCode.BadRequest, badRoot.Error);

        foreach (var badName in new[] { "", "  ", "a/b", ".", "..", new string('a', 256) })
        {
            var invalid = await store.SubmitAsync(new RenameItem("f1", badName, T0.AddMinutes(1)));
            Assert.False(invalid.Success);
            Assert.Equal(ErrorCode.BadRequest, invalid.Error);
        }

        // Same-name rename stays valid and idempotent.
        var same = await store.SubmitAsync(new RenameItem("f1", "docs", T0.AddMinutes(2)));
        Assert.True(same.Success);
        Assert.Equal(linesBefore + 1, ReadLogLines(dir).Length);
    }

    [Fact]
    public async Task MoveItem_MovesSubtreeAndUpdatesPaths_RejectsCyclesAndDuplicates()
    {
        var dir = NewDir();
        var store = Open(dir);

        await store.SubmitAsync(Folder("f-a", "a"));
        await store.SubmitAsync(Folder("f-b", "b", "f-a"));
        await store.SubmitAsync(Folder("f-c", "c", "f-b"));
        await store.SubmitAsync(Folder("f-dest", "dest"));
        await store.SubmitAsync(Folder("f-dup", "b", "f-dest"));

        // Moving folder into itself or descendant is BadRequest.
        var selfMove = await store.SubmitAsync(new MoveItem("f-a", "f-a", T0.AddMinutes(1)));
        Assert.False(selfMove.Success);
        Assert.Equal(ErrorCode.BadRequest, selfMove.Error);

        var childMove = await store.SubmitAsync(new MoveItem("f-a", "f-b", T0.AddMinutes(1)));
        Assert.False(childMove.Success);
        Assert.Equal(ErrorCode.BadRequest, childMove.Error);

        var grandChildMove = await store.SubmitAsync(new MoveItem("f-a", "f-c", T0.AddMinutes(1)));
        Assert.False(grandChildMove.Success);
        Assert.Equal(ErrorCode.BadRequest, grandChildMove.Error);

        // Moving into folder containing same name is AlreadyExists.
        var dupMove = await store.SubmitAsync(new MoveItem("f-b", "f-dest", T0.AddMinutes(1)));
        Assert.False(dupMove.Success);
        Assert.Equal(ErrorCode.AlreadyExists, dupMove.Error);

        // Moving missing or to missing fails with NotFound.
        Assert.Equal(ErrorCode.NotFound, (await store.SubmitAsync(new MoveItem("missing", "f-dest", T0))).Error);
        Assert.Equal(ErrorCode.NotFound, (await store.SubmitAsync(new MoveItem("f-b", "missing", T0))).Error);
        Assert.Equal(ErrorCode.BadRequest, (await store.SubmitAsync(new MoveItem(RootId, "f-dest", T0))).Error);

        // Valid move of /a/b -> /dest/b... no, move /a -> /dest/a succeeds and carries /dest/a/b/c.
        var tMove = T0.AddMinutes(10);
        var moved = await store.SubmitAsync(new MoveItem("f-a", "f-dest", tMove));
        Assert.True(moved.Success);

        Assert.Null(await store.FindItemByPathAsync("/a"));
        Assert.NotNull(await store.FindItemByPathAsync("/dest/a"));
        Assert.NotNull(await store.FindItemByPathAsync("/dest/a/b/c"));

        // Move back to root.
        Assert.True((await store.SubmitAsync(new MoveItem("f-a", RootId, T0.AddMinutes(11)))).Success);
        Assert.NotNull(await store.FindItemByPathAsync("/a/b/c"));
    }

    [Fact]
    public async Task MoveItem_IntoFileDestination_ReturnsBadRequest()
    {
        var dir = NewDir();
        var store = Open(dir);
        await store.SubmitAsync(Folder("f1", "docs"));
        Close(store);

        SeedFileWithVersionAndGrant(dir, "file-1", RootId, "readme.txt");
        store = Open(dir);

        var moveIntoFile = await store.SubmitAsync(new MoveItem("f1", "file-1", T0.AddMinutes(1)));
        Assert.False(moveIntoFile.Success);
        Assert.Equal(ErrorCode.BadRequest, moveIntoFile.Error);
    }

    [Fact]
    public async Task DeleteItem_DeletesSubtreeCascadesVersionsGrantsAndAllowsReuseOfName()
    {
        var dir = NewDir();
        var store = Open(dir);

        await store.SubmitAsync(Folder("f-root", "projects"));
        await store.SubmitAsync(Folder("f-sub", "sub", "f-root"));
        await store.SubmitAsync(Folder("f-keep", "keep"));
        Close(store);

        SeedFileWithVersionAndGrant(dir, "file-1", "f-sub", "spec.pdf");
        store = Open(dir);

        Assert.Equal(ErrorCode.NotFound, (await store.SubmitAsync(new DeleteItem("missing", T0))).Error);
        Assert.Equal(ErrorCode.BadRequest, (await store.SubmitAsync(new DeleteItem(RootId, T0))).Error);

        var del = await store.SubmitAsync(new DeleteItem("f-root", T0.AddMinutes(5)));
        Assert.True(del.Success);

        Assert.Null(await store.FindItemByIdAsync("f-root"));
        Assert.Null(await store.FindItemByIdAsync("f-sub"));
        Assert.Null(await store.FindItemByIdAsync("file-1"));
        Assert.NotNull(await store.FindItemByPathAsync("/keep"));

        // Creating a folder with the deleted name under root works.
        Assert.True((await store.SubmitAsync(Folder("f-root-2", "projects", RootId, T0.AddMinutes(6)))).Success);
        Close(store);

        Assert.Equal(0, CountRows(dir, "file_versions", "item_id", "file-1"));
        Assert.Equal(0, CountRows(dir, "version_chunks", "item_id", "file-1"));
        Assert.Equal(0, CountRows(dir, "grants", "item_id", "file-1"));
        Assert.Equal(0, CountRows(dir, "uploads", "item_id", "file-1"));
    }

    [Fact]
    public async Task OpLogReplay_ProducesIdenticalNamespaceStateAcrossStores()
    {
        var dir = NewDir();
        var store = Open(dir);

        await store.SubmitAsync(Folder("f1", "a", RootId, T0));
        await store.SubmitAsync(Folder("f2", "b", "f1", T0.AddSeconds(1)));
        await store.SubmitAsync(Folder("f3", "target", RootId, T0.AddSeconds(2)));
        await store.SubmitAsync(Folder("f4", "temp", "f1", T0.AddSeconds(3)));
        await store.SubmitAsync(new RenameItem("f2", "b-renamed", T0.AddSeconds(4)));
        await store.SubmitAsync(new MoveItem("f1", "f3", T0.AddSeconds(5)));
        await store.SubmitAsync(new DeleteItem("f4", T0.AddSeconds(6)));

        var expectedA = await store.FindItemByPathAsync("/target/a");
        var expectedB = await store.FindItemByPathAsync("/target/a/b-renamed");
        var expectedDeleted = await store.FindItemByIdAsync("f4");
        Close(store);

        var replayDir = NewDir();
        File.Copy(LogPath(dir), LogPath(replayDir));
        var replayed = Open(replayDir);

        Assert.Equal(expectedA, await replayed.FindItemByPathAsync("/target/a"));
        Assert.Equal(expectedB, await replayed.FindItemByPathAsync("/target/a/b-renamed"));
        Assert.Null(expectedDeleted);
        Assert.Null(await replayed.FindItemByIdAsync("f4"));
    }
}

