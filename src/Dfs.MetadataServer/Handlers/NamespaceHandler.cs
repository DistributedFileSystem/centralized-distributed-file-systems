using Dfs.MetadataServer.Store;
using Dfs.Shared.Commands;
using Dfs.Shared.Framing;
using Dfs.Shared.Messages;
using Dfs.Shared.Metadata;
using Dfs.Shared.Security;
using CommandCreateFolder = Dfs.Shared.Commands.CreateFolder;
using CommandDeleteItem = Dfs.Shared.Commands.DeleteItem;
using CreateFolderRequest = Dfs.Shared.Messages.CreateFolder;
using DeleteItemRequest = Dfs.Shared.Messages.DeleteItem;

namespace Dfs.MetadataServer.Handlers;

/// <summary>Handles namespace protocol requests against <see cref="IMetadataStore"/>.</summary>
public sealed class NamespaceHandler
{
    private const string DefaultUserId = "system";

    private static readonly ItemInfo RootItem = new(
        SqliteMetadataState.RootId,
        "/",
        "/",
        ItemKind.Folder,
        0,
        DefaultUserId,
        0,
        DateTime.UnixEpoch);

    private readonly IMetadataStore _store;
    private readonly IAuthorizer? _authorizer;

    /// <summary>Initializes a new instance of <see cref="NamespaceHandler"/>.</summary>
    /// <param name="store">The metadata store backing all queries and deterministic commands.</param>
    /// <param name="authorizer">Optional authorizer used to enforce RBAC permissions when configured.</param>
    public NamespaceHandler(IMetadataStore store, IAuthorizer? authorizer = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _authorizer = authorizer;
    }

    /// <summary>Returns true when <paramref name="type"/> is a supported namespace message type.</summary>
    public static bool CanHandle(MessageType type) => type is
        MessageType.ListDir or
        MessageType.Stat or
        MessageType.CreateFolder or
        MessageType.Rename or
        MessageType.Move or
        MessageType.DeleteItem;

    /// <summary>Dispatches a raw namespace request frame and returns the encoded response frame.</summary>
    public async Task<RawFrame> HandleAsync(RawFrame frame, CancellationToken cancellationToken = default)
    {
        switch (frame.Type)
        {
            case MessageType.ListDir:
            {
                var req = FrameSerialization.DeserializeJson<ListDir>(frame.Payload);
                var res = await ListDirAsync(req, cancellationToken).ConfigureAwait(false);
                return new RawFrame(MessageType.ListDirResponse, FrameSerialization.SerializeJson(res));
            }
            case MessageType.Stat:
            {
                var req = FrameSerialization.DeserializeJson<Stat>(frame.Payload);
                var res = await StatAsync(req, cancellationToken).ConfigureAwait(false);
                return new RawFrame(MessageType.StatResponse, FrameSerialization.SerializeJson(res));
            }
            case MessageType.CreateFolder:
            {
                var req = FrameSerialization.DeserializeJson<CreateFolderRequest>(frame.Payload);
                var res = await CreateFolderAsync(req, cancellationToken).ConfigureAwait(false);
                return new RawFrame(MessageType.CreateFolderResponse, FrameSerialization.SerializeJson(res));
            }
            case MessageType.Rename:
            {
                var req = FrameSerialization.DeserializeJson<Rename>(frame.Payload);
                var res = await RenameAsync(req, cancellationToken).ConfigureAwait(false);
                return new RawFrame(MessageType.RenameResponse, FrameSerialization.SerializeJson(res));
            }
            case MessageType.Move:
            {
                var req = FrameSerialization.DeserializeJson<Move>(frame.Payload);
                var res = await MoveAsync(req, cancellationToken).ConfigureAwait(false);
                return new RawFrame(MessageType.MoveResponse, FrameSerialization.SerializeJson(res));
            }
            case MessageType.DeleteItem:
            {
                var req = FrameSerialization.DeserializeJson<DeleteItemRequest>(frame.Payload);
                var res = await DeleteItemAsync(req, cancellationToken).ConfigureAwait(false);
                return new RawFrame(MessageType.DeleteItemResponse, FrameSerialization.SerializeJson(res));
            }
            default:
                throw new DfsException(ErrorCode.BadRequest, $"Unsupported namespace message type {frame.Type}.");
        }
    }

    /// <summary>Lists direct children of a folder path.</summary>
    public async Task<ListDirResponse> ListDirAsync(ListDir request, CancellationToken cancellationToken = default)
    {
        var userId = await ResolveUserAsync(request.RequestId, request.Session, cancellationToken).ConfigureAwait(false);
        if (!NamespacePath.TryParseAbsolute(request.Path, out var segments))
        {
            throw new DfsException(ErrorCode.BadRequest, $"Invalid path '{request.Path}'.");
        }

        string folderId;
        if (segments.Length == 0)
        {
            await AuthorizeAsync(userId, Permission.ItemList, null, cancellationToken).ConfigureAwait(false);
            folderId = SqliteMetadataState.RootId;
        }
        else
        {
            var item = await FindRequiredItemAsync(request.Path, cancellationToken).ConfigureAwait(false);
            await AuthorizeAsync(userId, Permission.ItemList, item.Id, cancellationToken).ConfigureAwait(false);
            if (item.Kind != ItemKind.Folder)
            {
                throw new DfsException(ErrorCode.BadRequest, $"Path '{request.Path}' is not a folder.");
            }
            folderId = item.Id;
        }

        var children = await _store.ListChildrenAsync(folderId, cancellationToken).ConfigureAwait(false);
        return new ListDirResponse(request.RequestId, new ValueList<ItemInfo>(children));
    }

    /// <summary>Returns metadata for an item path (or the synthetic root item for <c>"/"</c>).</summary>
    public async Task<StatResponse> StatAsync(Stat request, CancellationToken cancellationToken = default)
    {
        var userId = await ResolveUserAsync(request.RequestId, request.Session, cancellationToken).ConfigureAwait(false);
        if (!NamespacePath.TryParseAbsolute(request.Path, out var segments))
        {
            throw new DfsException(ErrorCode.BadRequest, $"Invalid path '{request.Path}'.");
        }

        if (segments.Length == 0)
        {
            await AuthorizeAsync(userId, Permission.ItemList, null, cancellationToken).ConfigureAwait(false);
            return new StatResponse(request.RequestId, RootItem);
        }

        var item = await FindRequiredItemAsync(request.Path, cancellationToken).ConfigureAwait(false);
        await AuthorizeAsync(userId, Permission.ItemList, item.Id, cancellationToken).ConfigureAwait(false);
        return new StatResponse(request.RequestId, item);
    }

    /// <summary>Creates a new folder via deterministic command submission to <see cref="IMetadataStore"/>.</summary>
    public async Task<CreateFolderResponse> CreateFolderAsync(CreateFolderRequest request, CancellationToken cancellationToken = default)
    {
        var userId = await ResolveUserAsync(request.RequestId, request.Session, cancellationToken).ConfigureAwait(false);
        if (!NamespacePath.TrySplitParent(request.Path, out var parentPath, out var name))
        {
            throw new DfsException(ErrorCode.BadRequest, $"Invalid folder path '{request.Path}'.");
        }

        var (parentId, parentAuthId) = await ResolveFolderAsync(parentPath, cancellationToken).ConfigureAwait(false);
        await AuthorizeAsync(userId, Permission.ItemCreate, parentAuthId, cancellationToken).ConfigureAwait(false);

        var itemId = "i-" + Guid.NewGuid().ToString("N");
        var cmd = new CommandCreateFolder(itemId, parentId, name, userId, DateTime.UtcNow);
        await SubmitOrThrowAsync(cmd, "Failed to create folder.", cancellationToken).ConfigureAwait(false);

        var created = await GetItemByIdOrThrowAsync(itemId, cancellationToken).ConfigureAwait(false);
        return new CreateFolderResponse(request.RequestId, created);
    }

    /// <summary>Renames an existing file or folder via deterministic command submission to <see cref="IMetadataStore"/>.</summary>
    public async Task<RenameResponse> RenameAsync(Rename request, CancellationToken cancellationToken = default)
    {
        var userId = await ResolveUserAsync(request.RequestId, request.Session, cancellationToken).ConfigureAwait(false);
        if (!NamespacePath.TrySplitParent(request.Path, out _, out _))
        {
            throw new DfsException(ErrorCode.BadRequest, $"Invalid item path '{request.Path}'.");
        }

        if (request.NewName is null || !NamespacePath.IsValidSegment(request.NewName))
        {
            throw new DfsException(ErrorCode.BadRequest, $"Invalid new name '{request.NewName}'.");
        }

        var item = await FindRequiredItemAsync(request.Path, cancellationToken).ConfigureAwait(false);
        await AuthorizeAsync(userId, Permission.ItemRename, item.Id, cancellationToken).ConfigureAwait(false);

        var cmd = new RenameItem(item.Id, request.NewName, DateTime.UtcNow);
        await SubmitOrThrowAsync(cmd, "Failed to rename item.", cancellationToken).ConfigureAwait(false);

        var updated = await GetItemByIdOrThrowAsync(item.Id, cancellationToken).ConfigureAwait(false);
        return new RenameResponse(request.RequestId, updated);
    }

    /// <summary>Moves an existing file or folder to another folder via deterministic command submission to <see cref="IMetadataStore"/>.</summary>
    public async Task<MoveResponse> MoveAsync(Move request, CancellationToken cancellationToken = default)
    {
        var userId = await ResolveUserAsync(request.RequestId, request.Session, cancellationToken).ConfigureAwait(false);
        if (!NamespacePath.TrySplitParent(request.Path, out _, out _))
        {
            throw new DfsException(ErrorCode.BadRequest, $"Invalid source path '{request.Path}'.");
        }

        if (!NamespacePath.TryParseAbsolute(request.NewParentPath, out _))
        {
            throw new DfsException(ErrorCode.BadRequest, $"Invalid destination path '{request.NewParentPath}'.");
        }

        var item = await FindRequiredItemAsync(request.Path, cancellationToken).ConfigureAwait(false);
        var (newParentId, destAuthId) = await ResolveFolderAsync(request.NewParentPath, cancellationToken).ConfigureAwait(false);

        await AuthorizeAsync(userId, Permission.ItemRename, item.Id, cancellationToken).ConfigureAwait(false);
        await AuthorizeAsync(userId, Permission.ItemCreate, destAuthId, cancellationToken).ConfigureAwait(false);

        var cmd = new MoveItem(item.Id, newParentId, DateTime.UtcNow);
        await SubmitOrThrowAsync(cmd, "Failed to move item.", cancellationToken).ConfigureAwait(false);

        var updated = await GetItemByIdOrThrowAsync(item.Id, cancellationToken).ConfigureAwait(false);
        return new MoveResponse(request.RequestId, updated);
    }

    /// <summary>Deletes a file or folder subtree via deterministic command submission to <see cref="IMetadataStore"/>.</summary>
    public async Task<DeleteItemResponse> DeleteItemAsync(DeleteItemRequest request, CancellationToken cancellationToken = default)
    {
        var userId = await ResolveUserAsync(request.RequestId, request.Session, cancellationToken).ConfigureAwait(false);
        if (!NamespacePath.TrySplitParent(request.Path, out _, out _))
        {
            throw new DfsException(ErrorCode.BadRequest, $"Invalid item path '{request.Path}'.");
        }

        var item = await FindRequiredItemAsync(request.Path, cancellationToken).ConfigureAwait(false);
        await AuthorizeAsync(userId, Permission.ItemDelete, item.Id, cancellationToken).ConfigureAwait(false);

        var cmd = new CommandDeleteItem(item.Id, DateTime.UtcNow);
        await SubmitOrThrowAsync(cmd, "Failed to delete item.", cancellationToken).ConfigureAwait(false);

        return new DeleteItemResponse(request.RequestId);
    }

    private async Task<string> ResolveUserAsync(string? requestId, string? session, CancellationToken cancellationToken)
    {
        if (requestId is null)
        {
            throw new DfsException(ErrorCode.BadRequest, "Missing requestId.");
        }

        if (session is null)
        {
            throw new DfsException(ErrorCode.BadRequest, "Missing session.");
        }

        if (string.IsNullOrWhiteSpace(session))
        {
            throw new DfsException(ErrorCode.Unauthorized, "Session token is required.");
        }

        var record = await _store.FindSessionAsync(session, cancellationToken).ConfigureAwait(false);
        if (record is not null)
        {
            if (record.ExpiresAt <= DateTime.UtcNow)
            {
                throw new DfsException(ErrorCode.Unauthorized, "Session has expired.");
            }

            return record.UserId;
        }

        if (_authorizer is not null)
        {
            throw new DfsException(ErrorCode.Unauthorized, "Invalid or expired session.");
        }

        return DefaultUserId;
    }

    private async Task<ItemInfo> FindRequiredItemAsync(string path, CancellationToken cancellationToken) =>
        await _store.FindItemByPathAsync(path, cancellationToken).ConfigureAwait(false)
        ?? throw new DfsException(ErrorCode.NotFound, $"Path '{path}' not found.");

    private async Task<(string FolderId, string? AuthItemId)> ResolveFolderAsync(string path, CancellationToken cancellationToken)
    {
        if (path == "/")
        {
            return (SqliteMetadataState.RootId, null);
        }

        var folder = await _store.FindItemByPathAsync(path, cancellationToken).ConfigureAwait(false)
            ?? throw new DfsException(ErrorCode.NotFound, $"Folder '{path}' not found.");

        if (folder.Kind != ItemKind.Folder)
        {
            throw new DfsException(ErrorCode.BadRequest, $"Path '{path}' is not a folder.");
        }

        return (folder.Id, folder.Id);
    }

    private async Task AuthorizeAsync(string userId, string permission, string? itemId, CancellationToken cancellationToken)
    {
        if (_authorizer is null)
        {
            return;
        }

        var result = await _authorizer.AuthorizeAsync(userId, permission, itemId, cancellationToken).ConfigureAwait(false);
        if (!result.Allowed)
        {
            throw new DfsException(ErrorCode.Forbidden, result.Reason);
        }
    }

    private async Task SubmitOrThrowAsync(IMetadataCommand command, string defaultMessage, CancellationToken cancellationToken)
    {
        var result = await _store.SubmitAsync(command, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            throw new DfsException(result.Error ?? ErrorCode.BadRequest, result.Message ?? defaultMessage);
        }
    }

    private async Task<ItemInfo> GetItemByIdOrThrowAsync(string itemId, CancellationToken cancellationToken) =>
        await _store.FindItemByIdAsync(itemId, cancellationToken).ConfigureAwait(false)
        ?? throw new DfsException(ErrorCode.Internal, $"Item '{itemId}' was not found after commit.");
}

