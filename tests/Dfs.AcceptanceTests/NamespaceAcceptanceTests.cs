using Dfs.Shared.Framing;
using Dfs.Shared.Messages;
using Xunit;

namespace Dfs.AcceptanceTests;

[CollectionDefinition("MetadataServer")]
public class MetadataServerCollection : ICollectionFixture<MetadataServerFixture>
{
}

/// <summary>
/// Black-box acceptance tests for Task M2-15 (Issue #33) — Namespace Operations over raw TCP.
/// Tests strictly according to docs/protocol.md using RawTcpTestClient.
/// </summary>
[Collection("MetadataServer")]
public class NamespaceAcceptanceTests
{
    private readonly MetadataServerFixture _fixture;

    public NamespaceAcceptanceTests(MetadataServerFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<(RawTcpTestClient Client, string Session)> CreateAuthenticatedClientAsync(
        CancellationToken cancellationToken = default)
    {
        var client = await RawTcpTestClient.ConnectAsync(_fixture.Host, _fixture.Port, cancellationToken);
        var helloRes = await client.HandshakeAsync(cancellationToken: cancellationToken);
        Assert.NotNull(helloRes);
        Assert.Equal(1, helloRes.ProtocolVersion);

        var loginReq = new Login(Guid.NewGuid().ToString("D"), _fixture.AdminUser, _fixture.AdminPassword);
        await client.SendMessageAsync(MessageType.Login, loginReq, cancellationToken);

        var loginRes = await client.ReceiveMessageAsync<LoginResponse>(MessageType.LoginResponse, cancellationToken);
        Assert.NotNull(loginRes);
        Assert.False(string.IsNullOrWhiteSpace(loginRes.Session));

        return (client, loginRes.Session);
    }

    private static async Task<ErrorResponse> ExpectErrorAsync<TRequest>(
        RawTcpTestClient client,
        MessageType requestType,
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : class
    {
        await client.SendMessageAsync(requestType, request, cancellationToken);
        var raw = await client.ReceiveRawFrameAsync(cancellationToken);
        Assert.NotNull(raw);
        Assert.Equal(MessageType.Error, raw.Type);

        var error = FrameSerialization.DeserializeJson<ErrorResponse>(raw.Payload);
        Assert.NotNull(error);
        return error;
    }

    /// <summary>
    /// Acceptance Criterion 1 (Happy Path):
    /// Verifies the full lifecycle of all 6 namespace operations over TCP:
    /// CreateFolder -> Stat -> ListDir -> Rename -> Move -> DeleteItem.
    /// </summary>
    [Fact]
    public async Task Namespace_FullLifecycle_CreateFolder_Stat_ListDir_Rename_Move_DeleteItem_Succeeds()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var (client, session) = await CreateAuthenticatedClientAsync(cts.Token);
        await using var _ = client;

        var rootTestDir = $"/acc-life-{Guid.NewGuid():N}";

        // 1. CreateFolder
        var createRootReq = new CreateFolder(Guid.NewGuid().ToString("D"), session, rootTestDir);
        await client.SendMessageAsync(MessageType.CreateFolder, createRootReq, cts.Token);
        var createRootRes = await client.ReceiveMessageAsync<CreateFolderResponse>(MessageType.CreateFolderResponse, cts.Token);
        Assert.NotNull(createRootRes);
        Assert.Equal(rootTestDir, createRootRes.Item.Path);
        Assert.Equal(ItemKind.Folder, createRootRes.Item.Kind);

        var subDir = $"{rootTestDir}/alpha";
        var createSubReq = new CreateFolder(Guid.NewGuid().ToString("D"), session, subDir);
        await client.SendMessageAsync(MessageType.CreateFolder, createSubReq, cts.Token);
        var createSubRes = await client.ReceiveMessageAsync<CreateFolderResponse>(MessageType.CreateFolderResponse, cts.Token);
        Assert.NotNull(createSubRes);
        Assert.Equal(subDir, createSubRes.Item.Path);

        var nestedDir = $"{subDir}/beta";
        var createNestedReq = new CreateFolder(Guid.NewGuid().ToString("D"), session, nestedDir);
        await client.SendMessageAsync(MessageType.CreateFolder, createNestedReq, cts.Token);
        var createNestedRes = await client.ReceiveMessageAsync<CreateFolderResponse>(MessageType.CreateFolderResponse, cts.Token);
        Assert.NotNull(createNestedRes);
        Assert.Equal(nestedDir, createNestedRes.Item.Path);

        // 2. Stat
        var statReq = new Stat(Guid.NewGuid().ToString("D"), session, subDir);
        await client.SendMessageAsync(MessageType.Stat, statReq, cts.Token);
        var statRes = await client.ReceiveMessageAsync<StatResponse>(MessageType.StatResponse, cts.Token);
        Assert.NotNull(statRes);
        Assert.Equal(subDir, statRes.Item.Path);
        Assert.Equal("alpha", statRes.Item.Name);
        Assert.Equal(ItemKind.Folder, statRes.Item.Kind);
        Assert.Equal(0, statRes.Item.Size);

        // 3. ListDir
        var listReq = new ListDir(Guid.NewGuid().ToString("D"), session, rootTestDir);
        await client.SendMessageAsync(MessageType.ListDir, listReq, cts.Token);
        var listRes = await client.ReceiveMessageAsync<ListDirResponse>(MessageType.ListDirResponse, cts.Token);
        Assert.NotNull(listRes);
        Assert.Single(listRes.Items);
        Assert.Equal("alpha", listRes.Items[0].Name);

        // 4. Rename: /acc-life/alpha -> /acc-life/alpha-renamed
        var renameReq = new Rename(Guid.NewGuid().ToString("D"), session, subDir, "alpha-renamed");
        await client.SendMessageAsync(MessageType.Rename, renameReq, cts.Token);
        var renameRes = await client.ReceiveMessageAsync<RenameResponse>(MessageType.RenameResponse, cts.Token);
        Assert.NotNull(renameRes);
        var renamedSubDir = $"{rootTestDir}/alpha-renamed";
        Assert.Equal(renamedSubDir, renameRes.Item.Path);
        Assert.Equal("alpha-renamed", renameRes.Item.Name);

        // Verify descendant path was updated after rename
        var nestedStatAfterRename = new Stat(Guid.NewGuid().ToString("D"), session, $"{renamedSubDir}/beta");
        await client.SendMessageAsync(MessageType.Stat, nestedStatAfterRename, cts.Token);
        var nestedStatRes = await client.ReceiveMessageAsync<StatResponse>(MessageType.StatResponse, cts.Token);
        Assert.NotNull(nestedStatRes);
        Assert.Equal($"{renamedSubDir}/beta", nestedStatRes.Item.Path);

        // Old path must be NotFound
        var oldStatErr = await ExpectErrorAsync(client, MessageType.Stat, new Stat(Guid.NewGuid().ToString("D"), session, subDir), cts.Token);
        Assert.Equal(ErrorCode.NotFound, oldStatErr.Code);

        // 5. Move: Create destination folder and move /acc-life/alpha-renamed into it
        var destFolder = $"{rootTestDir}/target";
        await client.SendMessageAsync(MessageType.CreateFolder, new CreateFolder(Guid.NewGuid().ToString("D"), session, destFolder), cts.Token);
        var destRes = await client.ReceiveMessageAsync<CreateFolderResponse>(MessageType.CreateFolderResponse, cts.Token);
        Assert.NotNull(destRes);

        var moveReq = new Move(Guid.NewGuid().ToString("D"), session, renamedSubDir, destFolder);
        await client.SendMessageAsync(MessageType.Move, moveReq, cts.Token);
        var moveRes = await client.ReceiveMessageAsync<MoveResponse>(MessageType.MoveResponse, cts.Token);
        Assert.NotNull(moveRes);
        var movedSubDir = $"{destFolder}/alpha-renamed";
        Assert.Equal(movedSubDir, moveRes.Item.Path);

        // Verify ListDir on root now has only target
        var listAfterMove = new ListDir(Guid.NewGuid().ToString("D"), session, rootTestDir);
        await client.SendMessageAsync(MessageType.ListDir, listAfterMove, cts.Token);
        var listAfterMoveRes = await client.ReceiveMessageAsync<ListDirResponse>(MessageType.ListDirResponse, cts.Token);
        Assert.NotNull(listAfterMoveRes);
        Assert.Single(listAfterMoveRes.Items);
        Assert.Equal("target", listAfterMoveRes.Items[0].Name);

        // Verify ListDir on target has alpha-renamed
        var listTarget = new ListDir(Guid.NewGuid().ToString("D"), session, destFolder);
        await client.SendMessageAsync(MessageType.ListDir, listTarget, cts.Token);
        var listTargetRes = await client.ReceiveMessageAsync<ListDirResponse>(MessageType.ListDirResponse, cts.Token);
        Assert.NotNull(listTargetRes);
        Assert.Single(listTargetRes.Items);
        Assert.Equal("alpha-renamed", listTargetRes.Items[0].Name);

        // 6. DeleteItem: Delete rootTestDir and confirm cascading deletion of all descendants
        var deleteReq = new DeleteItem(Guid.NewGuid().ToString("D"), session, rootTestDir);
        await client.SendMessageAsync(MessageType.DeleteItem, deleteReq, cts.Token);
        var deleteRes = await client.ReceiveMessageAsync<DeleteItemResponse>(MessageType.DeleteItemResponse, cts.Token);
        Assert.NotNull(deleteRes);

        // Confirm root and descendants are now NotFound
        var statRootErr = await ExpectErrorAsync(client, MessageType.Stat, new Stat(Guid.NewGuid().ToString("D"), session, rootTestDir), cts.Token);
        Assert.Equal(ErrorCode.NotFound, statRootErr.Code);

        var statNestedErr = await ExpectErrorAsync(client, MessageType.Stat, new Stat(Guid.NewGuid().ToString("D"), session, movedSubDir), cts.Token);
        Assert.Equal(ErrorCode.NotFound, statNestedErr.Code);
    }

    /// <summary>
    /// Acceptance Test: CreateFolder returns AlreadyExists when creating a folder that already exists.
    /// </summary>
    [Fact]
    public async Task CreateFolder_DuplicateName_ReturnsAlreadyExists()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (client, session) = await CreateAuthenticatedClientAsync(cts.Token);
        await using var _ = client;

        var folderPath = $"/acc-dup-{Guid.NewGuid():N}";

        // First creation succeeds
        await client.SendMessageAsync(MessageType.CreateFolder, new CreateFolder("r-1", session, folderPath), cts.Token);
        var createRes = await client.ReceiveMessageAsync<CreateFolderResponse>(MessageType.CreateFolderResponse, cts.Token);
        Assert.NotNull(createRes);

        // Duplicate creation returns Error(AlreadyExists)
        var dupErr = await ExpectErrorAsync(client, MessageType.CreateFolder, new CreateFolder("r-2", session, folderPath), cts.Token);
        Assert.Equal(ErrorCode.AlreadyExists, dupErr.Code);
        Assert.Equal("r-2", dupErr.RequestId);

        // Connection remains alive and usable
        var statReq = new Stat("r-3", session, folderPath);
        await client.SendMessageAsync(MessageType.Stat, statReq, cts.Token);
        var validStat = await client.ReceiveMessageAsync<StatResponse>(MessageType.StatResponse, cts.Token);
        Assert.NotNull(validStat);
        Assert.Equal(folderPath, validStat.Item.Path);
    }

    /// <summary>
    /// Acceptance Test: CreateFolder returns NotFound when the parent directory does not exist.
    /// </summary>
    [Fact]
    public async Task CreateFolder_NonExistentParent_ReturnsNotFound()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (client, session) = await CreateAuthenticatedClientAsync(cts.Token);
        await using var _ = client;

        var path = $"/nonexistent-parent-{Guid.NewGuid():N}/child";
        var err = await ExpectErrorAsync(client, MessageType.CreateFolder, new CreateFolder("r-1", session, path), cts.Token);
        Assert.Equal(ErrorCode.NotFound, err.Code);
        Assert.Equal("r-1", err.RequestId);
    }

    /// <summary>
    /// Acceptance Test: Stat returns NotFound when querying a non-existent item.
    /// </summary>
    [Fact]
    public async Task Stat_NonExistentPath_ReturnsNotFound()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (client, session) = await CreateAuthenticatedClientAsync(cts.Token);
        await using var _ = client;

        var path = $"/ghost-{Guid.NewGuid():N}";
        var err = await ExpectErrorAsync(client, MessageType.Stat, new Stat("r-1", session, path), cts.Token);
        Assert.Equal(ErrorCode.NotFound, err.Code);
        Assert.Equal("r-1", err.RequestId);
    }

    /// <summary>
    /// Acceptance Test: ListDir returns NotFound when listing a non-existent directory.
    /// </summary>
    [Fact]
    public async Task ListDir_NonExistentFolder_ReturnsNotFound()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (client, session) = await CreateAuthenticatedClientAsync(cts.Token);
        await using var _ = client;

        var path = $"/ghost-dir-{Guid.NewGuid():N}";
        var err = await ExpectErrorAsync(client, MessageType.ListDir, new ListDir("r-1", session, path), cts.Token);
        Assert.Equal(ErrorCode.NotFound, err.Code);
        Assert.Equal("r-1", err.RequestId);
    }

    /// <summary>
    /// Acceptance Test: Rename returns AlreadyExists when renaming to an existing sibling's name.
    /// </summary>
    [Fact]
    public async Task Rename_DuplicateName_ReturnsAlreadyExists()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (client, session) = await CreateAuthenticatedClientAsync(cts.Token);
        await using var _ = client;

        var testRoot = $"/acc-renamedup-{Guid.NewGuid():N}";
        await client.SendMessageAsync(MessageType.CreateFolder, new CreateFolder("r-1", session, testRoot), cts.Token);
        await client.ReceiveMessageAsync<CreateFolderResponse>(MessageType.CreateFolderResponse, cts.Token);

        await client.SendMessageAsync(MessageType.CreateFolder, new CreateFolder("r-2", session, $"{testRoot}/alpha"), cts.Token);
        await client.ReceiveMessageAsync<CreateFolderResponse>(MessageType.CreateFolderResponse, cts.Token);

        await client.SendMessageAsync(MessageType.CreateFolder, new CreateFolder("r-3", session, $"{testRoot}/beta"), cts.Token);
        await client.ReceiveMessageAsync<CreateFolderResponse>(MessageType.CreateFolderResponse, cts.Token);

        // Attempt to rename beta -> alpha
        var err = await ExpectErrorAsync(client, MessageType.Rename, new Rename("r-4", session, $"{testRoot}/beta", "alpha"), cts.Token);
        Assert.Equal(ErrorCode.AlreadyExists, err.Code);
        Assert.Equal("r-4", err.RequestId);
    }

    /// <summary>
    /// Acceptance Test: Rename returns NotFound when renaming an item that does not exist.
    /// </summary>
    [Fact]
    public async Task Rename_NonExistentItem_ReturnsNotFound()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (client, session) = await CreateAuthenticatedClientAsync(cts.Token);
        await using var _ = client;

        var err = await ExpectErrorAsync(client, MessageType.Rename, new Rename("r-1", session, $"/ghost-{Guid.NewGuid():N}", "new-name"), cts.Token);
        Assert.Equal(ErrorCode.NotFound, err.Code);
    }

    /// <summary>
    /// Acceptance Test: Move returns BadRequest when attempting a cyclic move (into itself or a descendant).
    /// </summary>
    [Fact]
    public async Task Move_CyclicMove_ReturnsBadRequest()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (client, session) = await CreateAuthenticatedClientAsync(cts.Token);
        await using var _ = client;

        var testRoot = $"/acc-cyclic-{Guid.NewGuid():N}";
        await client.SendMessageAsync(MessageType.CreateFolder, new CreateFolder("r-1", session, testRoot), cts.Token);
        await client.ReceiveMessageAsync<CreateFolderResponse>(MessageType.CreateFolderResponse, cts.Token);

        var parent = $"{testRoot}/parent";
        await client.SendMessageAsync(MessageType.CreateFolder, new CreateFolder("r-2", session, parent), cts.Token);
        await client.ReceiveMessageAsync<CreateFolderResponse>(MessageType.CreateFolderResponse, cts.Token);

        var child = $"{parent}/child";
        await client.SendMessageAsync(MessageType.CreateFolder, new CreateFolder("r-3", session, child), cts.Token);
        await client.ReceiveMessageAsync<CreateFolderResponse>(MessageType.CreateFolderResponse, cts.Token);

        // 1. Move into itself -> BadRequest
        var selfMoveErr = await ExpectErrorAsync(client, MessageType.Move, new Move("r-4", session, parent, parent), cts.Token);
        Assert.Equal(ErrorCode.BadRequest, selfMoveErr.Code);

        // 2. Move into descendant -> BadRequest
        var descendantMoveErr = await ExpectErrorAsync(client, MessageType.Move, new Move("r-5", session, parent, child), cts.Token);
        Assert.Equal(ErrorCode.BadRequest, descendantMoveErr.Code);
    }

    /// <summary>
    /// Acceptance Test: Move returns NotFound when either the source item or the destination parent does not exist.
    /// </summary>
    [Fact]
    public async Task Move_NonExistentSourceOrDestination_ReturnsNotFound()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (client, session) = await CreateAuthenticatedClientAsync(cts.Token);
        await using var _ = client;

        var testRoot = $"/acc-movenotfound-{Guid.NewGuid():N}";
        await client.SendMessageAsync(MessageType.CreateFolder, new CreateFolder("r-1", session, testRoot), cts.Token);
        await client.ReceiveMessageAsync<CreateFolderResponse>(MessageType.CreateFolderResponse, cts.Token);

        // Source does not exist
        var missingSourceErr = await ExpectErrorAsync(client, MessageType.Move, new Move("r-2", session, $"/ghost-{Guid.NewGuid():N}", testRoot), cts.Token);
        Assert.Equal(ErrorCode.NotFound, missingSourceErr.Code);

        // Destination does not exist
        var missingDestErr = await ExpectErrorAsync(client, MessageType.Move, new Move("r-3", session, testRoot, $"/ghost-dest-{Guid.NewGuid():N}"), cts.Token);
        Assert.Equal(ErrorCode.NotFound, missingDestErr.Code);
    }

    /// <summary>
    /// Acceptance Test: DeleteItem returns NotFound when attempting to delete a non-existent path.
    /// </summary>
    [Fact]
    public async Task DeleteItem_NonExistentPath_ReturnsNotFound()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (client, session) = await CreateAuthenticatedClientAsync(cts.Token);
        await using var _ = client;

        var err = await ExpectErrorAsync(client, MessageType.DeleteItem, new DeleteItem("r-1", session, $"/ghost-del-{Guid.NewGuid():N}"), cts.Token);
        Assert.Equal(ErrorCode.NotFound, err.Code);
    }

    /// <summary>
    /// Acceptance Test: DeleteItem on the root directory "/" is rejected with BadRequest.
    /// </summary>
    [Fact]
    public async Task DeleteItem_RootDirectory_ReturnsBadRequest()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (client, session) = await CreateAuthenticatedClientAsync(cts.Token);
        await using var _ = client;

        var err = await ExpectErrorAsync(client, MessageType.DeleteItem, new DeleteItem("r-1", session, "/"), cts.Token);
        Assert.Equal(ErrorCode.BadRequest, err.Code);
    }

    /// <summary>
    /// Acceptance Test: Namespace operations without a valid session token return Unauthorized.
    /// </summary>
    [Fact]
    public async Task NamespaceOperations_WithoutValidSession_ReturnUnauthorized()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var client = await RawTcpTestClient.ConnectAsync(_fixture.Host, _fixture.Port, cts.Token);
        await client.HandshakeAsync(cancellationToken: cts.Token);

        const string invalidSession = "invalid-or-fake-session-token";

        // CreateFolder with invalid session
        var createErr = await ExpectErrorAsync(client, MessageType.CreateFolder, new CreateFolder("r-1", invalidSession, "/unauth-test"), cts.Token);
        Assert.Equal(ErrorCode.Unauthorized, createErr.Code);

        // Stat with invalid session
        var statErr = await ExpectErrorAsync(client, MessageType.Stat, new Stat("r-2", invalidSession, "/"), cts.Token);
        Assert.Equal(ErrorCode.Unauthorized, statErr.Code);

        // ListDir with invalid session
        var listErr = await ExpectErrorAsync(client, MessageType.ListDir, new ListDir("r-3", invalidSession, "/"), cts.Token);
        Assert.Equal(ErrorCode.Unauthorized, listErr.Code);

        // Rename with invalid session
        var renameErr = await ExpectErrorAsync(client, MessageType.Rename, new Rename("r-4", invalidSession, "/a", "b"), cts.Token);
        Assert.Equal(ErrorCode.Unauthorized, renameErr.Code);

        // Move with invalid session
        var moveErr = await ExpectErrorAsync(client, MessageType.Move, new Move("r-5", invalidSession, "/a", "/b"), cts.Token);
        Assert.Equal(ErrorCode.Unauthorized, moveErr.Code);

        // DeleteItem with invalid session
        var deleteErr = await ExpectErrorAsync(client, MessageType.DeleteItem, new DeleteItem("r-6", invalidSession, "/a"), cts.Token);
        Assert.Equal(ErrorCode.Unauthorized, deleteErr.Code);
    }
}
