using System.Net.Sockets;
using Dfs.MetadataServer.Config;
using Dfs.MetadataServer.Handlers;
using Dfs.MetadataServer.Hosting;
using Dfs.MetadataServer.Security;
using Dfs.MetadataServer.Store;
using Dfs.Shared.Commands;
using Dfs.Shared.Framing;
using Dfs.Shared.Messages;
using Dfs.Shared.Metadata;
using Dfs.Shared.Security;
using Microsoft.Data.Sqlite;
using CreateFolder = Dfs.Shared.Messages.CreateFolder;
using DeleteItem = Dfs.Shared.Messages.DeleteItem;

namespace Dfs.Tests.Metadata;

/// <summary>End-to-end TCP and handler tests for namespace protocol operations in <see cref="Dfs.MetadataServer"/>.</summary>
public sealed class NamespaceHandlerTests : IDisposable
{
    private const string Session = "sess-1";
    private readonly string _tempDir;
    private readonly List<MetadataServerHost> _hosts = new();

    public NamespaceHandlerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dfs-ns-host-" + Guid.NewGuid().ToString("N"));
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

    private MetadataServerConfig CreateConfig(string? dataDir = null) => new(
        NodeRole: "metadata",
        NodeId: "meta-ns-test",
        ListenPort: 0,
        AdvertiseHost: "127.0.0.1",
        MetadataEndpoints: new[] { "127.0.0.1:0" },
        DataDir: dataDir ?? _tempDir,
        TokenKey: "test-token-key",
        AdminUser: "admin",
        AdminPassword: "pwd");

    private MetadataServerHost CreateHost(IMetadataStore? store = null, IAuthorizer? authorizer = null, string? dataDir = null)
    {
        var host = new MetadataServerHost(CreateConfig(dataDir), store, authorizer);
        _hosts.Add(host);
        return host;
    }

    private async Task<MetadataServerHost> CreateAndStartHostAsync(IMetadataStore? store = null, IAuthorizer? authorizer = null, string? dataDir = null)
    {
        var host = CreateHost(store, authorizer, dataDir);
        await host.StartAsync();
        var admin = await host.Store.FindUserByNameAsync(host.Config.AdminUser);
        if (admin is not null)
        {
            await host.Store.SubmitAsync(new CreateSession(Session, admin.Id, DateTime.UtcNow.AddDays(1), DateTime.UtcNow));
        }
        return host;
    }

    private static async Task<(TcpClient Client, NetworkStream Stream)> ConnectAndHelloAsync(MetadataServerHost host)
    {
        var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", host.BoundPort);
        var stream = client.GetStream();
        var hello = new Hello(Guid.NewGuid().ToString("D"), 1, NodeType.Client);
        await HandshakeHandler.ClientHandshakeAsync(stream, hello);
        return (client, stream);
    }

    private static async Task<TResponse> RequestAsync<TRequest, TResponse>(
        Stream stream,
        MessageType requestType,
        MessageType responseType,
        TRequest request)
        where TResponse : class
    {
        await FrameCodec.WriteMessageAsync(stream, requestType, request);
        var response = await FrameCodec.ReadMessageAsync<TResponse>(stream, responseType);
        Assert.NotNull(response);
        return response;
    }

    private static async Task<ErrorResponse> RequestExpectErrorAsync<TRequest>(
        Stream stream,
        MessageType requestType,
        TRequest request)
    {
        await FrameCodec.WriteMessageAsync(stream, requestType, request);
        var frame = await FrameCodec.ReadRawFrameAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal(MessageType.Error, frame.Type);
        return FrameSerialization.DeserializeJson<ErrorResponse>(frame.Payload);
    }

    private static void SeedFile(string dataDir, string itemId, string parentId, string name)
    {
        using var conn = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = Path.Combine(dataDir, "metadata.db"), Pooling = false }.ToString());
        conn.Open();
        MetadataSchema.Initialize(conn);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO items (id, parent_id, name, kind, owner_id, current_version, size, modified_at)
              VALUES ($id, $p, $n, 'File', 'u-owner', 1, 42, '2026-11-10T09:00:00Z');
            """;
        cmd.Parameters.AddWithValue("$id", itemId);
        cmd.Parameters.AddWithValue("$p", parentId);
        cmd.Parameters.AddWithValue("$n", name);
        cmd.ExecuteNonQuery();
    }

    private static void SeedPrivateFile(string dataDir)
    {
        using var conn = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = Path.Combine(dataDir, "metadata.db"), Pooling = false }.ToString());
        conn.Open();
        MetadataSchema.Initialize(conn);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO items (id, parent_id, name, kind, owner_id, current_version, size, modified_at)
              VALUES ('private-folder', 'root', 'private', 'Folder', 'u-owner', 0, 0, '2026-11-10T09:00:00Z');
            INSERT INTO items (id, parent_id, name, kind, owner_id, current_version, size, modified_at)
              VALUES ('private-file', 'private-folder', 'file', 'File', 'u-owner', 1, 42, '2026-11-10T09:00:00Z');
            """;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public async Task TcpRoundTrip_CreateFolderStatListDirRenameMoveDeleteItem_Succeeds()
    {
        var host = await CreateAndStartHostAsync();

        var (client, stream) = await ConnectAndHelloAsync(host);
        await using var _ = stream;
        using var __ = client;

        // Stat root "/"
        var rootStat = await RequestAsync<Stat, StatResponse>(
            stream, MessageType.Stat, MessageType.StatResponse, new Stat("r-1", Session, "/"));
        Assert.Equal("r-1", rootStat.RequestId);
        Assert.Equal("/", rootStat.Item.Path);
        Assert.Equal(ItemKind.Folder, rootStat.Item.Kind);

        // Create /projects, /projects/dfs, /archive
        var mkProj = await RequestAsync<CreateFolder, CreateFolderResponse>(
            stream, MessageType.CreateFolder, MessageType.CreateFolderResponse,
            new CreateFolder("r-2", Session, "/projects"));
        Assert.Equal("/projects", mkProj.Item.Path);
        Assert.Equal(ItemKind.Folder, mkProj.Item.Kind);

        var mkDfs = await RequestAsync<CreateFolder, CreateFolderResponse>(
            stream, MessageType.CreateFolder, MessageType.CreateFolderResponse,
            new CreateFolder("r-3", Session, "/projects/dfs"));
        Assert.Equal("/projects/dfs", mkDfs.Item.Path);

        await RequestAsync<CreateFolder, CreateFolderResponse>(
            stream, MessageType.CreateFolder, MessageType.CreateFolderResponse,
            new CreateFolder("r-4", Session, "/archive"));

        // ListDir "/" returns sorted children: archive, projects
        var rootList = await RequestAsync<ListDir, ListDirResponse>(
            stream, MessageType.ListDir, MessageType.ListDirResponse,
            new ListDir("r-5", Session, "/"));
        Assert.Equal(new[] { "archive", "projects" }, rootList.Items.Select(i => i.Name));

        // ListDir "/projects" returns dfs
        var projList = await RequestAsync<ListDir, ListDirResponse>(
            stream, MessageType.ListDir, MessageType.ListDirResponse,
            new ListDir("r-6", Session, "/projects"));
        Assert.Single(projList.Items);
        Assert.Equal("dfs", projList.Items[0].Name);

        // Rename /projects/dfs -> dfs-v2
        var renamed = await RequestAsync<Rename, RenameResponse>(
            stream, MessageType.Rename, MessageType.RenameResponse,
            new Rename("r-7", Session, "/projects/dfs", "dfs-v2"));
        Assert.Equal("/projects/dfs-v2", renamed.Item.Path);
        Assert.Equal("dfs-v2", renamed.Item.Name);

        // Move /projects/dfs-v2 -> /archive
        var moved = await RequestAsync<Move, MoveResponse>(
            stream, MessageType.Move, MessageType.MoveResponse,
            new Move("r-8", Session, "/projects/dfs-v2", "/archive"));
        Assert.Equal("/archive/dfs-v2", moved.Item.Path);

        var projAfterMove = await RequestAsync<ListDir, ListDirResponse>(
            stream, MessageType.ListDir, MessageType.ListDirResponse,
            new ListDir("r-9", Session, "/projects"));
        Assert.Empty(projAfterMove.Items);

        // Delete /archive removes /archive and /archive/dfs-v2
        var deleted = await RequestAsync<DeleteItem, DeleteItemResponse>(
            stream, MessageType.DeleteItem, MessageType.DeleteItemResponse,
            new DeleteItem("r-10", Session, "/archive"));
        Assert.Equal("r-10", deleted.RequestId);

        var statDeleted = await RequestExpectErrorAsync(
            stream, MessageType.Stat, new Stat("r-11", Session, "/archive/dfs-v2"));
        Assert.Equal(ErrorCode.NotFound, statDeleted.Code);
        Assert.Equal("r-11", statDeleted.RequestId);
    }

    [Fact]
    public async Task TcpRoundTrip_DuplicateCycleInvalidPathsAndUnsupportedMessage_ReturnProtocolErrorsAndKeepConnectionOpen()
    {
        var dataDir = Path.Combine(_tempDir, "errors");
        Directory.CreateDirectory(dataDir);
        SeedFile(dataDir, "file-1", "root", "notes.txt");

        var host = await CreateAndStartHostAsync(dataDir: dataDir);

        var (client, stream) = await ConnectAndHelloAsync(host);
        await using var _ = stream;
        using var __ = client;

        await RequestAsync<CreateFolder, CreateFolderResponse>(
            stream, MessageType.CreateFolder, MessageType.CreateFolderResponse,
            new CreateFolder("setup-1", Session, "/a"));
        await RequestAsync<CreateFolder, CreateFolderResponse>(
            stream, MessageType.CreateFolder, MessageType.CreateFolderResponse,
            new CreateFolder("setup-2", Session, "/a/b"));
        await RequestAsync<CreateFolder, CreateFolderResponse>(
            stream, MessageType.CreateFolder, MessageType.CreateFolderResponse,
            new CreateFolder("setup-3", Session, "/other"));
        await RequestAsync<CreateFolder, CreateFolderResponse>(
            stream, MessageType.CreateFolder, MessageType.CreateFolderResponse,
            new CreateFolder("setup-4", Session, "/other/b"));

        // Duplicate CreateFolder -> AlreadyExists
        var dupCreate = await RequestExpectErrorAsync(
            stream, MessageType.CreateFolder, new CreateFolder("err-dup-1", Session, "/a"));
        Assert.Equal(ErrorCode.AlreadyExists, dupCreate.Code);
        Assert.Equal("err-dup-1", dupCreate.RequestId);

        // Duplicate Rename -> AlreadyExists
        var dupRename = await RequestExpectErrorAsync(
            stream, MessageType.Rename, new Rename("err-dup-2", Session, "/other", "a"));
        Assert.Equal(ErrorCode.AlreadyExists, dupRename.Code);

        // Duplicate Move -> AlreadyExists
        var dupMove = await RequestExpectErrorAsync(
            stream, MessageType.Move, new Move("err-dup-3", Session, "/a/b", "/other"));
        Assert.Equal(ErrorCode.AlreadyExists, dupMove.Code);

        // Move folder into itself or descendant -> BadRequest
        var selfMove = await RequestExpectErrorAsync(
            stream, MessageType.Move, new Move("err-cycle-1", Session, "/a", "/a"));
        Assert.Equal(ErrorCode.BadRequest, selfMove.Code);

        var childMove = await RequestExpectErrorAsync(
            stream, MessageType.Move, new Move("err-cycle-2", Session, "/a", "/a/b"));
        Assert.Equal(ErrorCode.BadRequest, childMove.Code);

        // File target errors: ListDir on file, CreateFolder under file, Move into file -> BadRequest
        Assert.Equal(ErrorCode.BadRequest, (await RequestExpectErrorAsync(
            stream, MessageType.ListDir, new ListDir("err-file-1", Session, "/notes.txt"))).Code);
        Assert.Equal(ErrorCode.BadRequest, (await RequestExpectErrorAsync(
            stream, MessageType.CreateFolder, new CreateFolder("err-file-2", Session, "/notes.txt/sub"))).Code);
        Assert.Equal(ErrorCode.BadRequest, (await RequestExpectErrorAsync(
            stream, MessageType.Move, new Move("err-file-3", Session, "/a", "/notes.txt"))).Code);

        // Invalid paths -> BadRequest
        foreach (var badPath in new[] { "", "relative", "/a/", "/a//b", "/.", "/a/..", "/" + new string('x', 256) })
        {
            var err = await RequestExpectErrorAsync(
                stream, MessageType.Stat, new Stat("err-path", Session, badPath));
            Assert.Equal(ErrorCode.BadRequest, err.Code);
        }

        // Cannot mutate root "/" -> BadRequest
        Assert.Equal(ErrorCode.BadRequest, (await RequestExpectErrorAsync(
            stream, MessageType.CreateFolder, new CreateFolder("err-root-1", Session, "/"))).Code);
        Assert.Equal(ErrorCode.BadRequest, (await RequestExpectErrorAsync(
            stream, MessageType.Rename, new Rename("err-root-2", Session, "/", "x"))).Code);
        Assert.Equal(ErrorCode.BadRequest, (await RequestExpectErrorAsync(
            stream, MessageType.Move, new Move("err-root-3", Session, "/", "/other"))).Code);
        Assert.Equal(ErrorCode.BadRequest, (await RequestExpectErrorAsync(
            stream, MessageType.DeleteItem, new DeleteItem("err-root-4", Session, "/"))).Code);

        // Missing path -> NotFound
        Assert.Equal(ErrorCode.NotFound, (await RequestExpectErrorAsync(
            stream, MessageType.ListDir, new ListDir("err-missing", Session, "/missing"))).Code);

        // Unsupported frame keeps connection alive; valid Stat still succeeds afterward.
        // Note: ReadChunk is a DataServer message, so sending it to MetadataServer is unsupported (BadRequest).
        var unsupported = await RequestExpectErrorAsync(
            stream, MessageType.ReadChunk, new ReadChunk("err-unsup", "chunk-1", "token-xyz"));
        Assert.Equal(ErrorCode.BadRequest, unsupported.Code);
        Assert.Equal("err-unsup", unsupported.RequestId);

        var okAfterErrors = await RequestAsync<Stat, StatResponse>(
            stream, MessageType.Stat, MessageType.StatResponse, new Stat("ok-final", Session, "/notes.txt"));
        Assert.Equal(ItemKind.File, okAfterErrors.Item.Kind);
    }

    [Fact]
    public async Task SessionAndOptionalAuthorizer_EnforceSessionOwnershipAndPermissions()
    {
        var dataDir = Path.Combine(_tempDir, "auth");
        Directory.CreateDirectory(dataDir);
        using var innerStore = new MetadataStore(Path.Combine(dataDir, "oplog.jsonl"), Path.Combine(dataDir, "metadata.db"));
        var store = new SessionStubStore(innerStore);
        store.Users["u-alice"] = new UserCredentials("u-alice", "alice", "hash", "salt", Disabled: false);
        store.Users["u-disabled"] = new UserCredentials("u-disabled", "disabled-user", "hash", "salt", Disabled: true);
        store.Sessions["tok-alice"] = new SessionRecord("tok-alice", "u-alice", DateTime.UtcNow.AddHours(1));
        store.Sessions["tok-expired"] = new SessionRecord("tok-expired", "u-alice", DateTime.UtcNow.AddMinutes(-5));
        store.Sessions["tok-disabled"] = new SessionRecord("tok-disabled", "u-disabled", DateTime.UtcNow.AddHours(1));

        var authorizer = new RecordingAuthorizer { Allow = true };
        var host = CreateHost(store: store, authorizer: authorizer, dataDir: dataDir);
        await host.StartAsync();

        var (client, stream) = await ConnectAndHelloAsync(host);
        await using var _ = stream;
        using var __ = client;

        // Valid session creates folder owned by resolved user and invokes IAuthorizer.
        var created = await RequestAsync<CreateFolder, CreateFolderResponse>(
            stream, MessageType.CreateFolder, MessageType.CreateFolderResponse,
            new CreateFolder("auth-1", "tok-alice", "/alice-docs"));
        Assert.Equal("u-alice", created.Item.OwnerId);
        Assert.Contains(("u-alice", Permission.ItemCreate, "root"), authorizer.Calls);

        // Unknown session rejected as Unauthorized.
        Assert.Equal(ErrorCode.Unauthorized, (await RequestExpectErrorAsync(
            stream, MessageType.Stat, new Stat("auth-2", "unknown-tok", "/alice-docs"))).Code);

        // Expired, disabled user, empty, or whitespace session rejected as Unauthorized.
        Assert.Equal(ErrorCode.Unauthorized, (await RequestExpectErrorAsync(
            stream, MessageType.Stat, new Stat("auth-3", "tok-expired", "/alice-docs"))).Code);
        Assert.Equal(ErrorCode.Unauthorized, (await RequestExpectErrorAsync(
            stream, MessageType.Stat, new Stat("auth-dis", "tok-disabled", "/alice-docs"))).Code);
        Assert.Equal(ErrorCode.Unauthorized, (await RequestExpectErrorAsync(
            stream, MessageType.Stat, new Stat("auth-4", "", "/alice-docs"))).Code);
        Assert.Equal(ErrorCode.Unauthorized, (await RequestExpectErrorAsync(
            stream, MessageType.Stat, new Stat("auth-spc", "   ", "/alice-docs"))).Code);

        // Whitespace or empty requestId rejected as BadRequest.
        Assert.Equal(ErrorCode.BadRequest, (await RequestExpectErrorAsync(
            stream, MessageType.Stat, new Stat("", "tok-alice", "/alice-docs"))).Code);
        Assert.Equal(ErrorCode.BadRequest, (await RequestExpectErrorAsync(
            stream, MessageType.Stat, new Stat("   ", "tok-alice", "/alice-docs"))).Code);

        // Denied authorization rejected as Forbidden.
        authorizer.Allow = false;
        var forbidden = await RequestExpectErrorAsync(
            stream, MessageType.DeleteItem, new DeleteItem("auth-5", "tok-alice", "/alice-docs"));
        Assert.Equal(ErrorCode.Forbidden, forbidden.Code);
    }

    [Fact]
    public async Task DefaultRbacAuthorizer_EnforcesNamespacePermissionsOverTcp()
    {
        SeedPrivateFile(_tempDir);
        var host = await CreateAndStartHostAsync();
        var (client, stream) = await ConnectAndHelloAsync(host);
        await using var _ = stream;
        using var __ = client;

        await RequestAsync<CreateFolder, CreateFolderResponse>(stream, MessageType.CreateFolder,
            MessageType.CreateFolderResponse, new CreateFolder("setup-1", Session, "/shared"));
        await RequestAsync<CreateFolder, CreateFolderResponse>(stream, MessageType.CreateFolder,
            MessageType.CreateFolderResponse, new CreateFolder("setup-2", Session, "/shared/docs"));
        var shared = (await host.Store.FindItemByPathAsync("/shared"))!;
        Assert.True((await new UserCredentialService(host.Store).CreateUserAsync(
            "u-bob", "bob", "password", DateTime.UtcNow)).Success);
        Assert.True((await host.Store.SubmitAsync(new CreateSession("bob-session", "u-bob",
            DateTime.UtcNow.AddHours(1), DateTime.UtcNow))).Success);
        Assert.True((await host.Store.SubmitAsync(new SetGrant("g-bob-viewer", BuiltInRoles.ViewerId,
            SubjectType.User, "u-bob", shared.Id, DateTime.UtcNow))).Success);

        Assert.Equal(ErrorCode.Forbidden, (await RequestExpectErrorAsync(stream, MessageType.ListDir,
            new ListDir("deny-root-list", "bob-session", "/"))).Code);
        Assert.Equal(ErrorCode.Forbidden, (await RequestExpectErrorAsync(stream, MessageType.Stat,
            new Stat("deny-root-stat", "bob-session", "/"))).Code);
        Assert.Equal(ErrorCode.Forbidden, (await RequestExpectErrorAsync(stream, MessageType.ClusterStatus,
            new ClusterStatus("deny-cluster-status", "bob-session"))).Code);
        var sharedList = await RequestAsync<ListDir, ListDirResponse>(stream, MessageType.ListDir,
            MessageType.ListDirResponse, new ListDir("list-shared", "bob-session", "/shared"));
        Assert.Equal(new[] { "/shared/docs" }, sharedList.Items.Select(item => item.Path));
        var child = await RequestAsync<Stat, StatResponse>(stream, MessageType.Stat,
            MessageType.StatResponse, new Stat("stat-child", "bob-session", "/shared/docs"));
        Assert.Equal("/shared/docs", child.Item.Path);

        Assert.Equal(ErrorCode.Forbidden, (await RequestExpectErrorAsync(stream, MessageType.Stat,
            new Stat("deny-stat", "bob-session", "/private"))).Code);
        Assert.Equal(ErrorCode.Forbidden, (await RequestExpectErrorAsync(stream, MessageType.ListDir,
            new ListDir("deny-list", "bob-session", "/private"))).Code);
        Assert.Equal(ErrorCode.Forbidden, (await RequestExpectErrorAsync(stream, MessageType.CreateFolder,
            new CreateFolder("deny-create", "bob-session", "/shared/new"))).Code);
        Assert.Equal(ErrorCode.Forbidden, (await RequestExpectErrorAsync(stream, MessageType.CreateFolder,
            new CreateFolder("deny-root-create", "bob-session", "/new"))).Code);
        Assert.Equal(ErrorCode.Forbidden, (await RequestExpectErrorAsync(stream, MessageType.Rename,
            new Rename("deny-rename", "bob-session", "/shared/docs", "renamed"))).Code);
        Assert.Equal(ErrorCode.Forbidden, (await RequestExpectErrorAsync(stream, MessageType.Move,
            new Move("deny-move", "bob-session", "/shared/docs", "/private"))).Code);
        Assert.Equal(ErrorCode.Forbidden, (await RequestExpectErrorAsync(stream, MessageType.DeleteItem,
            new DeleteItem("deny-delete", "bob-session", "/shared/docs"))).Code);
        Assert.Equal(ErrorCode.Forbidden, (await RequestExpectErrorAsync(stream, MessageType.CreateFolder,
            new CreateFolder("deny-file-parent", "bob-session", "/private/file/sub"))).Code);
        Assert.Equal(ErrorCode.Forbidden, (await RequestExpectErrorAsync(stream, MessageType.Move,
            new Move("deny-source-missing-destination", "bob-session", "/private/file", "/missing"))).Code);
        Assert.Equal(ErrorCode.Forbidden, (await RequestExpectErrorAsync(stream, MessageType.Move,
            new Move("deny-file-destination", "bob-session", "/shared/docs", "/private/file"))).Code);

        Assert.True((await host.Store.SubmitAsync(new SetGrant("g-bob-editor", BuiltInRoles.EditorId,
            SubjectType.User, "u-bob", shared.Id, DateTime.UtcNow))).Success);
        Assert.Equal(ErrorCode.Forbidden, (await RequestExpectErrorAsync(stream, MessageType.Move,
            new Move("deny-source-editor-destination", "bob-session", "/private/file", "/shared"))).Code);
        Assert.Equal(ErrorCode.Forbidden, (await RequestExpectErrorAsync(stream, MessageType.Move,
            new Move("deny-move-destination", "bob-session", "/shared/docs", "/"))).Code);

        var created = await RequestAsync<CreateFolder, CreateFolderResponse>(stream, MessageType.CreateFolder,
            MessageType.CreateFolderResponse, new CreateFolder("allow-create", "bob-session", "/shared/new"));
        Assert.Equal("u-bob", created.Item.OwnerId);
        await RequestAsync<CreateFolder, CreateFolderResponse>(stream, MessageType.CreateFolder,
            MessageType.CreateFolderResponse, new CreateFolder("allow-destination", "bob-session", "/shared/destination"));
        var renamed = await RequestAsync<Rename, RenameResponse>(stream, MessageType.Rename,
            MessageType.RenameResponse, new Rename("allow-rename", "bob-session", "/shared/new", "renamed"));
        Assert.Equal("/shared/renamed", renamed.Item.Path);
        var moved = await RequestAsync<Move, MoveResponse>(stream, MessageType.Move,
            MessageType.MoveResponse, new Move("allow-move", "bob-session", "/shared/renamed", "/shared/destination"));
        Assert.Equal("/shared/destination/renamed", moved.Item.Path);
        await RequestAsync<DeleteItem, DeleteItemResponse>(stream, MessageType.DeleteItem,
            MessageType.DeleteItemResponse, new DeleteItem("allow-delete", "bob-session", moved.Item.Path));
        Assert.Null(await host.Store.FindItemByPathAsync(moved.Item.Path));
    }

    [Fact]
    public async Task ListDir_FiltersChildrenDeniedByAuthorizer()
    {
        var authorizer = new RecordingAuthorizer();
        var host = await CreateAndStartHostAsync(authorizer: authorizer);
        var (client, stream) = await ConnectAndHelloAsync(host);
        await using var _ = stream;
        using var __ = client;

        foreach (var path in new[] { "/shared", "/shared/visible", "/shared/hidden" })
        {
            await RequestAsync<CreateFolder, CreateFolderResponse>(stream, MessageType.CreateFolder,
                MessageType.CreateFolderResponse, new CreateFolder(Guid.NewGuid().ToString("N"), Session, path));
        }

        var hidden = (await host.Store.FindItemByPathAsync("/shared/hidden"))!;
        authorizer.DeniedItemIds.Add(hidden.Id);
        var listing = await RequestAsync<ListDir, ListDirResponse>(stream, MessageType.ListDir,
            MessageType.ListDirResponse, new ListDir("filtered-list", Session, "/shared"));
        Assert.Equal(new[] { "/shared/visible" }, listing.Items.Select(item => item.Path));
        Assert.Contains(authorizer.Calls, call => call.Permission == Permission.ItemList && call.ItemId == hidden.Id);
        Assert.Contains(await host.Store.ListAuditAsync(null, null, null, 100), entry =>
            entry.Action == nameof(MessageType.ListDir) && entry.Target == "/shared/hidden" && !entry.Allowed);
    }

    [Fact]
    public async Task DefaultAuthorizer_StillRejectsInvalidSession()
    {
        var dataDir = Path.Combine(_tempDir, "no-authorizer");
        Directory.CreateDirectory(dataDir);
        var host = CreateHost(dataDir: dataDir);
        await host.StartAsync();

        var (client, stream) = await ConnectAndHelloAsync(host);
        await using var _ = stream;
        using var __ = client;

        // Unknown session is rejected before the default authorizer runs.
        var errFake = await RequestExpectErrorAsync(
            stream, MessageType.Stat, new Stat("noauth-1", "fake-token", "/"));
        Assert.Equal(ErrorCode.Unauthorized, errFake.Code);

        // Empty session rejected as Unauthorized
        var errEmpty = await RequestExpectErrorAsync(
            stream, MessageType.Stat, new Stat("noauth-2", "", "/"));
        Assert.Equal(ErrorCode.Unauthorized, errEmpty.Code);
    }

    [Fact]
    public async Task ListVersions_ReturnsRetainedVersionsDescending_AndEnforcesRbac()
    {
        var host = await CreateAndStartHostAsync();
        var (client, stream) = await ConnectAndHelloAsync(host);
        await using var _ = stream;
        using var __ = client;

        await RequestAsync<CreateFolder, CreateFolderResponse>(stream, MessageType.CreateFolder,
            MessageType.CreateFolderResponse, new CreateFolder("lv-folder", Session, "/docs"));
        var folder = (await host.Store.FindItemByPathAsync("/docs"))!;

        Assert.True((await host.Store.SubmitAsync(new RegisterDataServer("ds-1", "127.0.0.1:7001", 10000, 10000, DateTime.UtcNow))).Success);
        var sha = new string('a', 64);

        for (var v = 1; v <= 3; v++)
        {
            var uploadId = $"up-lv-{v}";
            var chunkId = $"c-lv-{v}";
            Assert.True((await host.Store.SubmitAsync(new Dfs.Shared.Commands.BeginUpload(
                uploadId, "item-lv-1", folder.Id, "report.pdf", "admin",
                v * 100L, v - 1, 4096,
                new ValueList<PlannedChunk>([new PlannedChunk(0, chunkId, new ValueList<string>(["127.0.0.1:7001"]))]),
                DateTime.UtcNow))).Success);
            Assert.True((await host.Store.SubmitAsync(new Dfs.Shared.Commands.CommitUpload(
                uploadId,
                new ValueList<ChecksumInfo>([new ChecksumInfo(chunkId, v * 100L, sha)]),
                KeepVersions: 5,
                DateTime.UtcNow))).Success);
        }

        // Admin receives all 3 versions ordered newest first
        var lvRes = await RequestAsync<ListVersions, ListVersionsResponse>(stream, MessageType.ListVersions,
            MessageType.ListVersionsResponse, new ListVersions("lv-admin", Session, "/docs/report.pdf"));
        Assert.Equal([3, 2, 1], lvRes.Versions.Select(v => v.Version));
        Assert.Equal([300L, 200L, 100L], lvRes.Versions.Select(v => v.Size));

        // Folder path -> BadRequest; root path -> BadRequest; missing path -> NotFound
        var errFolder = await RequestExpectErrorAsync(stream, MessageType.ListVersions, new ListVersions("lv-dir", Session, "/docs"));
        Assert.Equal(ErrorCode.BadRequest, errFolder.Code);
        var errRoot = await RequestExpectErrorAsync(stream, MessageType.ListVersions, new ListVersions("lv-root", Session, "/"));
        Assert.Equal(ErrorCode.BadRequest, errRoot.Code);
        var errMissing = await RequestExpectErrorAsync(stream, MessageType.ListVersions, new ListVersions("lv-miss", Session, "/docs/missing.pdf"));
        Assert.Equal(ErrorCode.NotFound, errMissing.Code);

        // Unauthorized user -> Forbidden; after Viewer grant -> Allowed
        Assert.True((await new UserCredentialService(host.Store).CreateUserAsync("u-reader", "reader", "password", DateTime.UtcNow)).Success);
        Assert.True((await host.Store.SubmitAsync(new CreateSession("reader-sess", "u-reader", DateTime.UtcNow.AddHours(1), DateTime.UtcNow))).Success);

        var errForbidden = await RequestExpectErrorAsync(stream, MessageType.ListVersions, new ListVersions("lv-deny", "reader-sess", "/docs/report.pdf"));
        Assert.Equal(ErrorCode.Forbidden, errForbidden.Code);

        Assert.True((await host.Store.SubmitAsync(new SetGrant("g-reader-view", BuiltInRoles.ViewerId, SubjectType.User, "u-reader", "item-lv-1", DateTime.UtcNow))).Success);
        var readerRes = await RequestAsync<ListVersions, ListVersionsResponse>(stream, MessageType.ListVersions,
            MessageType.ListVersionsResponse, new ListVersions("lv-allow", "reader-sess", "/docs/report.pdf"));
        Assert.Equal(3, readerRes.Versions.Count);
    }

    [Fact]
    public async Task Audit_RecordsAllowedAndDeniedRequests_AndRestrictsListAudit()
    {
        var host = await CreateAndStartHostAsync();
        var (client, stream) = await ConnectAndHelloAsync(host);
        await using var _ = stream;
        using var __ = client;

        await RequestAsync<CreateFolder, CreateFolderResponse>(stream, MessageType.CreateFolder,
            MessageType.CreateFolderResponse, new CreateFolder("audit-create", Session, "/audited"));
        Assert.True((await new UserCredentialService(host.Store).CreateUserAsync(
            "u-bob", "bob", "password", DateTime.UtcNow)).Success);
        Assert.True((await host.Store.SubmitAsync(new CreateSession("bob-audit-session", "u-bob",
            DateTime.UtcNow.AddHours(1), DateTime.UtcNow))).Success);
        Assert.Equal(ErrorCode.Forbidden, (await RequestExpectErrorAsync(stream, MessageType.CreateFolder,
            new CreateFolder("audit-denied", "bob-audit-session", "/denied"))).Code);

        var entries = await RequestAsync<ListAudit, ListAuditResponse>(stream, MessageType.ListAudit,
            MessageType.ListAuditResponse, new ListAudit("audit-list", Session));
        Assert.Equal(2, entries.Entries.Count);
        Assert.Equal("CreateFolder", entries.Entries[0].Action);
        Assert.False(entries.Entries[0].Allowed);
        Assert.Equal("/denied", entries.Entries[0].Target);
        Assert.True(entries.Entries[1].Allowed);
        Assert.Equal("/audited", entries.Entries[1].Target);

        var filtered = await RequestAsync<ListAudit, ListAuditResponse>(stream, MessageType.ListAudit,
            MessageType.ListAuditResponse, new ListAudit("audit-filter", Session, UserId: "u-bob", Limit: 1));
        Assert.Single(filtered.Entries);
        Assert.False(filtered.Entries[0].Allowed);
        Assert.Equal(ErrorCode.Forbidden, (await RequestExpectErrorAsync(stream, MessageType.ListAudit,
            new ListAudit("audit-forbidden", "bob-audit-session"))).Code);
        Assert.Equal(ErrorCode.BadRequest, (await RequestExpectErrorAsync(stream, MessageType.ListAudit,
            new ListAudit("audit-invalid-limit", Session, Limit: 1001))).Code);

        Assert.True((await host.Store.SubmitAsync(new SetGrant("g-bob-auditor", BuiltInRoles.AuditorId,
            SubjectType.User, "u-bob", null, DateTime.UtcNow))).Success);
        var auditorEntries = await RequestAsync<ListAudit, ListAuditResponse>(stream, MessageType.ListAudit,
            MessageType.ListAuditResponse, new ListAudit("auditor-list", "bob-audit-session"));
        Assert.Equal(3, auditorEntries.Entries.Count);
        Assert.Equal("ListAudit", auditorEntries.Entries[0].Action);
        Assert.False(auditorEntries.Entries[0].Allowed);

        using var replayed = new MetadataStore(Path.Combine(_tempDir, "oplog.jsonl"),
            Path.Combine(_tempDir, "audit-replayed.db"));
        var replayedEntries = await replayed.ListAuditAsync(null, null, null, 100);
        Assert.Equal(auditorEntries.Entries, replayedEntries);
    }

    private sealed class RecordingAuthorizer : IAuthorizer
    {
        public bool Allow { get; set; } = true;
        public HashSet<string> DeniedItemIds { get; } = new(StringComparer.Ordinal);
        public List<(string UserId, string Permission, string? ItemId)> Calls { get; } = new();

        public Task<AuthorizationResult> AuthorizeAsync(string userId, string permission, string? itemId, CancellationToken cancellationToken = default)
        {
            Calls.Add((userId, permission, itemId));
            return Task.FromResult(Allow && (itemId is null || !DeniedItemIds.Contains(itemId))
                ? AuthorizationResult.Allow("ok") : AuthorizationResult.Deny("denied"));
        }
    }

    private sealed class SessionStubStore(IMetadataStore inner) : IMetadataStore
    {
        public Dictionary<string, SessionRecord> Sessions { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, UserCredentials> Users { get; } = new(StringComparer.Ordinal);

        public Task<CommandResult> SubmitAsync(IMetadataCommand command, CancellationToken cancellationToken = default) =>
            inner.SubmitAsync(command, cancellationToken);

        public Task<ItemInfo?> FindItemByPathAsync(string path, CancellationToken cancellationToken = default) =>
            inner.FindItemByPathAsync(path, cancellationToken);

        public Task<ItemInfo?> FindItemByIdAsync(string itemId, CancellationToken cancellationToken = default) =>
            inner.FindItemByIdAsync(itemId, cancellationToken);

        public Task<IReadOnlyList<ItemInfo>> ListChildrenAsync(string folderId, CancellationToken cancellationToken = default) =>
            inner.ListChildrenAsync(folderId, cancellationToken);

        public Task<IReadOnlyList<VersionInfo>> ListVersionsAsync(string itemId, CancellationToken cancellationToken = default) =>
            inner.ListVersionsAsync(itemId, cancellationToken);

        public Task<IReadOnlyList<ChunkRecord>> GetVersionChunksAsync(string itemId, int version, CancellationToken cancellationToken = default) =>
            inner.GetVersionChunksAsync(itemId, version, cancellationToken);

        public Task<UploadRecord?> FindUploadAsync(string uploadId, CancellationToken cancellationToken = default) =>
            inner.FindUploadAsync(uploadId, cancellationToken);

        public Task<UserCredentials?> FindUserByNameAsync(string username, CancellationToken cancellationToken = default) =>
            inner.FindUserByNameAsync(username, cancellationToken);

        public Task<UserCredentials?> FindUserByIdAsync(string userId, CancellationToken cancellationToken = default) =>
            Users.TryGetValue(userId, out var user)
                ? Task.FromResult<UserCredentials?>(user)
                : inner.FindUserByIdAsync(userId, cancellationToken);

        public Task<IReadOnlyList<UserInfo>> ListUsersAsync(CancellationToken cancellationToken = default) =>
            inner.ListUsersAsync(cancellationToken);

        public Task<IReadOnlyList<GroupInfo>> ListGroupsAsync(CancellationToken cancellationToken = default) =>
            inner.ListGroupsAsync(cancellationToken);

        public Task<IReadOnlyList<RoleInfo>> ListRolesAsync(CancellationToken cancellationToken = default) =>
            inner.ListRolesAsync(cancellationToken);

        public Task<IReadOnlyList<GrantInfo>> ListGrantsAsync(string? itemId, string? subjectId, bool systemOnly, CancellationToken cancellationToken = default) =>
            inner.ListGrantsAsync(itemId, subjectId, systemOnly, cancellationToken);

        public Task<SessionRecord?> FindSessionAsync(string token, CancellationToken cancellationToken = default) =>
            Task.FromResult(Sessions.GetValueOrDefault(token));

        public Task<IReadOnlyList<DataServerInfo>> ListDataServersAsync(CancellationToken cancellationToken = default) =>
            inner.ListDataServersAsync(cancellationToken);

        public Task<IReadOnlyList<AuditEntry>> ListAuditAsync(DateTime? from, DateTime? to, string? userId, int limit, CancellationToken cancellationToken = default) =>
            inner.ListAuditAsync(from, to, userId, limit, cancellationToken);
    }
}

