using System.Text.Json;
using Dfs.MetadataServer.Security;
using Dfs.MetadataServer.Store;
using Dfs.Shared.Commands;
using Dfs.Shared.Framing;
using Dfs.Shared.Messages;
using Dfs.Shared.Metadata;
using Dfs.Shared.Security;
using CommandBeginUpload = Dfs.Shared.Commands.BeginUpload;
using CommandCommitUpload = Dfs.Shared.Commands.CommitUpload;
using CommandAbortUpload = Dfs.Shared.Commands.AbortUpload;
using CommitUploadRequest = Dfs.Shared.Messages.CommitUpload;
using AbortUploadRequest = Dfs.Shared.Messages.AbortUpload;

namespace Dfs.MetadataServer.Handlers;

/// <summary>
/// Handles CreateUpload, CommitUpload, AbortUpload, and Open protocol requests
/// against <see cref="IMetadataStore"/>.
/// </summary>
public sealed class TransferHandler
{
    private readonly IMetadataStore _store;
    private readonly IAuthorizer? _authorizer;
    private readonly DataTokenSigner _tokenSigner;
    private readonly TimeProvider _timeProvider;
    private readonly int _defaultChunkSize;
    private readonly long _maxFileSize;

    /// <summary>
    /// Initializes a new instance of <see cref="TransferHandler"/>.
    /// </summary>
    public TransferHandler(
        IMetadataStore store,
        DataTokenSigner tokenSigner,
        IAuthorizer? authorizer = null,
        TimeProvider? timeProvider = null,
        int defaultChunkSize = 4 * 1024 * 1024,
        long maxFileSize = 2L * 1024 * 1024 * 1024)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _tokenSigner = tokenSigner ?? throw new ArgumentNullException(nameof(tokenSigner));
        _authorizer = authorizer;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _defaultChunkSize = defaultChunkSize;
        _maxFileSize = maxFileSize;
    }

    /// <summary>Returns true when the message type is handled by this handler.</summary>
    public static bool CanHandle(MessageType type) => type is
        MessageType.CreateUpload or
        MessageType.CommitUpload or
        MessageType.AbortUpload or
        MessageType.Open;

    /// <summary>Dispatches a transfer frame and returns the encoded response.</summary>
    public async Task<RawFrame> HandleAsync(RawFrame frame, CancellationToken cancellationToken = default)
    {
        switch (frame.Type)
        {
            case MessageType.CreateUpload:
            {
                var req = FrameSerialization.DeserializeJson<CreateUpload>(frame.Payload);
                var res = await CreateUploadAsync(req, cancellationToken).ConfigureAwait(false);
                return new RawFrame(MessageType.CreateUploadResponse, FrameSerialization.SerializeJson(res));
            }
            case MessageType.CommitUpload:
            {
                var req = FrameSerialization.DeserializeJson<CommitUploadRequest>(frame.Payload);
                var res = await CommitUploadAsync(req, cancellationToken).ConfigureAwait(false);
                return new RawFrame(MessageType.CommitUploadResponse, FrameSerialization.SerializeJson(res));
            }
            case MessageType.AbortUpload:
            {
                var req = FrameSerialization.DeserializeJson<AbortUploadRequest>(frame.Payload);
                var res = await AbortUploadAsync(req, cancellationToken).ConfigureAwait(false);
                return new RawFrame(MessageType.AbortUploadResponse, FrameSerialization.SerializeJson(res));
            }
            case MessageType.Open:
            {
                var req = FrameSerialization.DeserializeJson<Open>(frame.Payload);
                var res = await OpenAsync(req, cancellationToken).ConfigureAwait(false);
                return new RawFrame(MessageType.OpenResponse, FrameSerialization.SerializeJson(res));
            }
            default:
                throw new DfsException(ErrorCode.BadRequest, $"Unsupported transfer message type {frame.Type}.");
        }
    }

    /// <summary>Handles CreateUpload: allocates chunks on alive data servers and returns write tokens.</summary>
    public async Task<CreateUploadResponse> CreateUploadAsync(CreateUpload request, CancellationToken cancellationToken = default)
    {
        var userId = await ResolveUserAsync(request.RequestId, request.Session, cancellationToken).ConfigureAwait(false);

        if (!NamespacePath.TryParseAbsolute(request.Path, out var segments) || segments.Length == 0)
        {
            throw new DfsException(ErrorCode.BadRequest, $"Invalid file path '{request.Path}'.");
        }

        if (request.Size < 0)
        {
            throw new DfsException(ErrorCode.BadRequest, "Size cannot be negative.");
        }

        if (request.Size > _maxFileSize)
        {
            throw new DfsException(ErrorCode.BadRequest, $"File size exceeds limit of {_maxFileSize} bytes.");
        }

        if (request.BaseVersion < 0)
        {
            throw new DfsException(ErrorCode.BadRequest, "BaseVersion cannot be negative.");
        }

        var normalizedPath = "/" + string.Join('/', segments);
        var existingItem = await _store.FindItemByPathAsync(normalizedPath, cancellationToken).ConfigureAwait(false);

        string itemId;
        string parentId;
        string fileName = segments[^1];

        if (existingItem is not null)
        {
            if (existingItem.Kind != ItemKind.File)
            {
                throw new DfsException(ErrorCode.BadRequest, $"Path '{normalizedPath}' is a folder.");
            }

            if (existingItem.CurrentVersion != request.BaseVersion)
            {
                throw new DfsException(ErrorCode.Conflict, $"BaseVersion {request.BaseVersion} does not match current version {existingItem.CurrentVersion}.", VersionDetails(existingItem.CurrentVersion));
            }

            await AuthorizeAsync(userId, Permission.FileWrite, existingItem.Id, cancellationToken).ConfigureAwait(false);
            itemId = existingItem.Id;

            var parentPath = segments.Length == 1 ? "/" : "/" + string.Join('/', segments[..^1]);
            parentId = parentPath == "/" ? SqliteMetadataState.RootId : (await _store.FindItemByPathAsync(parentPath, cancellationToken).ConfigureAwait(false))?.Id ?? SqliteMetadataState.RootId;
        }
        else
        {
            if (request.BaseVersion != 0)
            {
                throw new DfsException(ErrorCode.Conflict, "BaseVersion must be 0 for a new file.", VersionDetails(0));
            }

            var parentPath = segments.Length == 1 ? "/" : "/" + string.Join('/', segments[..^1]);
            if (parentPath == "/")
            {
                await AuthorizeAsync(userId, Permission.ItemCreate, null, cancellationToken).ConfigureAwait(false);
                parentId = SqliteMetadataState.RootId;
            }
            else
            {
                var parent = await _store.FindItemByPathAsync(parentPath, cancellationToken).ConfigureAwait(false);
                if (parent is null)
                {
                    throw new DfsException(ErrorCode.NotFound, $"Parent folder '{parentPath}' not found.");
                }
                if (parent.Kind != ItemKind.Folder)
                {
                    throw new DfsException(ErrorCode.BadRequest, $"Parent path '{parentPath}' is not a folder.");
                }

                await AuthorizeAsync(userId, Permission.ItemCreate, parent.Id, cancellationToken).ConfigureAwait(false);
                parentId = parent.Id;
            }

            itemId = Guid.NewGuid().ToString("D");
        }

        // Data Server Allocation ("Chọn Data Server theo chỗ trống")
        var allServers = await _store.ListDataServersAsync(cancellationToken).ConfigureAwait(false);
        var aliveServers = allServers.Where(s => s.Status == DataServerStatus.Alive).ToList();
        if (aliveServers.Count == 0)
        {
            throw new DfsException(ErrorCode.Unavailable, "No alive data servers available.");
        }

        var chunkCount = request.Size == 0 ? 0 : (int)Math.Ceiling((double)request.Size / _defaultChunkSize);
        var serverFreeMap = aliveServers.ToDictionary(s => s.Address, s => s.FreeBytes);

        var plannedChunks = new List<PlannedChunk>(chunkCount);
        var uploadChunks = new List<UploadChunk>(chunkCount);

        for (var i = 0; i < chunkCount; i++)
        {
            var chunkBytes = (int)Math.Min((long)_defaultChunkSize, request.Size - (long)i * _defaultChunkSize);
            var bestServer = serverFreeMap.OrderByDescending(kv => kv.Value).FirstOrDefault();
            if (bestServer.Key is null || bestServer.Value < chunkBytes)
            {
                throw new DfsException(ErrorCode.Unavailable, "Insufficient storage capacity on data servers.");
            }

            serverFreeMap[bestServer.Key] -= chunkBytes;

            var chunkId = $"c-{Guid.NewGuid():N}";
            var token = _tokenSigner.Sign(userId, chunkId, "w");
            var serversList = new ValueList<string>([bestServer.Key]);

            plannedChunks.Add(new PlannedChunk(i, chunkId, serversList));
            uploadChunks.Add(new UploadChunk(i, chunkId, serversList, token));
        }

        var uploadId = $"up-{Guid.NewGuid():N}";
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var cmd = new CommandBeginUpload(
            uploadId,
            itemId,
            parentId,
            fileName,
            userId,
            request.Size,
            request.BaseVersion,
            _defaultChunkSize,
            new ValueList<PlannedChunk>(plannedChunks),
            now);

        var result = await _store.SubmitAsync(cmd, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            throw new DfsException(result.Error ?? ErrorCode.BadRequest, result.Message ?? "Failed to begin upload.", result.Details);
        }

        return new CreateUploadResponse(request.RequestId, uploadId, _defaultChunkSize, new ValueList<UploadChunk>(uploadChunks));
    }

    /// <summary>Handles CommitUpload: verifies checksums, updates file version, and stores chunk metadata.</summary>
    public async Task<CommitUploadResponse> CommitUploadAsync(CommitUploadRequest request, CancellationToken cancellationToken = default)
    {
        var userId = await ResolveUserAsync(request.RequestId, request.Session, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(request.UploadId))
        {
            throw new DfsException(ErrorCode.BadRequest, "UploadId is required.");
        }

        var upload = await _store.FindUploadAsync(request.UploadId, cancellationToken).ConfigureAwait(false);
        if (upload is null)
        {
            throw new DfsException(ErrorCode.NotFound, $"Upload '{request.UploadId}' not found.");
        }

        var existingItem = await _store.FindItemByIdAsync(upload.ItemId, cancellationToken).ConfigureAwait(false);
        if (existingItem is not null)
        {
            await AuthorizeAsync(userId, Permission.FileWrite, existingItem.Id, cancellationToken).ConfigureAwait(false);
            if (existingItem.CurrentVersion != upload.BaseVersion)
            {
                throw new DfsException(ErrorCode.Conflict, $"File version has changed to {existingItem.CurrentVersion}.", VersionDetails(existingItem.CurrentVersion));
            }
        }
        else
        {
            var authFolderId = upload.ParentId == SqliteMetadataState.RootId ? null : upload.ParentId;
            await AuthorizeAsync(userId, Permission.ItemCreate, authFolderId, cancellationToken).ConfigureAwait(false);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var cmd = new CommandCommitUpload(request.UploadId, request.Checksums, KeepVersions: 0, now);

        var result = await _store.SubmitAsync(cmd, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            throw new DfsException(result.Error ?? ErrorCode.BadRequest, result.Message ?? "Failed to commit upload.", result.Details);
        }

        var item = await _store.FindItemByIdAsync(upload.ItemId, cancellationToken).ConfigureAwait(false)
            ?? throw new DfsException(ErrorCode.Internal, "Committed file could not be retrieved.");

        return new CommitUploadResponse(request.RequestId, item, item.CurrentVersion);
    }

    /// <summary>Handles AbortUpload: drops in-flight upload record.</summary>
    public async Task<AbortUploadResponse> AbortUploadAsync(AbortUploadRequest request, CancellationToken cancellationToken = default)
    {
        var userId = await ResolveUserAsync(request.RequestId, request.Session, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(request.UploadId))
        {
            throw new DfsException(ErrorCode.BadRequest, "UploadId is required.");
        }

        var upload = await _store.FindUploadAsync(request.UploadId, cancellationToken).ConfigureAwait(false);
        if (upload is null)
        {
            throw new DfsException(ErrorCode.NotFound, $"Upload '{request.UploadId}' not found.");
        }

        if (upload.CreatedBy != userId)
        {
            await AuthorizeAsync(userId, Permission.UserManage, null, cancellationToken).ConfigureAwait(false);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var cmd = new CommandAbortUpload(request.UploadId, now);

        var result = await _store.SubmitAsync(cmd, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            throw new DfsException(result.Error ?? ErrorCode.BadRequest, result.Message ?? "Failed to abort upload.", result.Details);
        }

        return new AbortUploadResponse(request.RequestId);
    }

    /// <summary>Handles Open: returns chunk locations with signed read tokens.</summary>
    public async Task<OpenResponse> OpenAsync(Open request, CancellationToken cancellationToken = default)
    {
        var userId = await ResolveUserAsync(request.RequestId, request.Session, cancellationToken).ConfigureAwait(false);

        if (!NamespacePath.TryParseAbsolute(request.Path, out var segments) || segments.Length == 0)
        {
            throw new DfsException(ErrorCode.BadRequest, $"Invalid file path '{request.Path}'.");
        }

        var normalizedPath = "/" + string.Join('/', segments);
        var item = await _store.FindItemByPathAsync(normalizedPath, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            throw new DfsException(ErrorCode.NotFound, $"File '{normalizedPath}' not found.");
        }

        if (item.Kind != ItemKind.File)
        {
            throw new DfsException(ErrorCode.BadRequest, $"Path '{normalizedPath}' is not a file.");
        }

        await AuthorizeAsync(userId, Permission.FileRead, item.Id, cancellationToken).ConfigureAwait(false);

        var targetVersion = request.Version > 0 ? request.Version : item.CurrentVersion;
        if (targetVersion <= 0 || targetVersion > item.CurrentVersion)
        {
            throw new DfsException(ErrorCode.NotFound, $"Version {targetVersion} not found for file '{normalizedPath}'.");
        }

        var chunkRecords = await _store.GetVersionChunksAsync(item.Id, targetVersion, cancellationToken).ConfigureAwait(false);
        if (chunkRecords.Count == 0 && item.Size > 0)
        {
            throw new DfsException(ErrorCode.NotFound, $"Version {targetVersion} chunks not found for file '{normalizedPath}'.");
        }

        var allServers = await _store.ListDataServersAsync(cancellationToken).ConfigureAwait(false);
        var aliveAddresses = allServers
            .Where(s => s.Status == DataServerStatus.Alive)
            .Select(s => s.Address)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var openChunks = new List<OpenChunk>(chunkRecords.Count);
        long totalSize = 0;

        foreach (var chunk in chunkRecords)
        {
            var aliveForChunk = chunk.Servers.Where(addr => aliveAddresses.Contains(addr)).ToList();
            if (aliveForChunk.Count == 0)
            {
                throw new DfsException(ErrorCode.Unavailable, $"All data servers holding chunk '{chunk.ChunkId}' are dead.");
            }

            var token = _tokenSigner.Sign(userId, chunk.ChunkId, "r");
            openChunks.Add(new OpenChunk(chunk.Index, chunk.ChunkId, chunk.Size, chunk.Sha256, new ValueList<string>(aliveForChunk), token));
            totalSize += chunk.Size;
        }

        return new OpenResponse(request.RequestId, item, targetVersion, totalSize, _defaultChunkSize, new ValueList<OpenChunk>(openChunks));
    }

    private async Task<string> ResolveUserAsync(string? requestId, string? session, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            throw new DfsException(ErrorCode.BadRequest, "RequestId is required.");
        }

        if (string.IsNullOrWhiteSpace(session))
        {
            throw new DfsException(ErrorCode.Unauthorized, "Session token is required.");
        }

        var record = await _store.FindSessionAsync(session, cancellationToken).ConfigureAwait(false);
        if (record is null || record.ExpiresAt <= _timeProvider.GetUtcNow().UtcDateTime)
        {
            throw new DfsException(ErrorCode.Unauthorized, "Invalid or expired session.");
        }

        var user = await _store.FindUserByIdAsync(record.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null || user.Disabled)
        {
            throw new DfsException(ErrorCode.Unauthorized, "User does not exist or has been disabled.");
        }

        return record.UserId;
    }

    private async Task AuthorizeAsync(string userId, string permission, string? itemId, CancellationToken cancellationToken)
    {
        if (_authorizer is null) return;

        var result = await _authorizer.AuthorizeAsync(userId, permission, itemId, cancellationToken).ConfigureAwait(false);
        if (!result.Allowed)
        {
            throw new DfsException(ErrorCode.Forbidden, result.Reason);
        }
    }

    private static Dictionary<string, JsonElement> VersionDetails(int currentVersion)
    {
        using var doc = JsonDocument.Parse($"{{\"currentVersion\":{currentVersion}}}");
        return new Dictionary<string, JsonElement>
        {
            ["currentVersion"] = doc.RootElement.GetProperty("currentVersion").Clone()
        };
    }
}
