using Dfs.Shared.Commands;
using Dfs.Shared.Messages;

namespace Dfs.Shared.Metadata;

/// <summary>The only boundary to the metadata state. All changes go through <see cref="SubmitAsync"/>; every other member only reads. Stage 1 writes an op log then applies to SQLite; stage 2 submits to Raft.</summary>
public interface IMetadataStore
{
    /// <summary>The single write path: logs the command, then applies it. Rejected commands are returned as a failed result, not thrown.</summary>
    /// <param name="command">The deterministic command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The outcome.</returns>
    Task<CommandResult> SubmitAsync(IMetadataCommand command, CancellationToken cancellationToken = default);

    /// <summary>Finds an item by absolute path.</summary>
    /// <param name="path">Absolute path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The item, or null when it does not exist.</returns>
    Task<ItemInfo?> FindItemByPathAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Finds an item by id.</summary>
    /// <param name="itemId">Id of the item.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The item, or null when it does not exist.</returns>
    Task<ItemInfo?> FindItemByIdAsync(string itemId, CancellationToken cancellationToken = default);

    /// <summary>Lists the children of a folder.</summary>
    /// <param name="folderId">Id of the folder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The children.</returns>
    Task<IReadOnlyList<ItemInfo>> ListChildrenAsync(string folderId, CancellationToken cancellationToken = default);

    /// <summary>Lists retained versions of a file, newest first.</summary>
    /// <param name="itemId">Id of the file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The versions.</returns>
    Task<IReadOnlyList<VersionInfo>> ListVersionsAsync(string itemId, CancellationToken cancellationToken = default);

    /// <summary>Gets the chunks of one version of a file.</summary>
    /// <param name="itemId">Id of the file.</param>
    /// <param name="version">Version number.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The chunks ordered by index; empty when the version does not exist.</returns>
    Task<IReadOnlyList<ChunkRecord>> GetVersionChunksAsync(string itemId, int version, CancellationToken cancellationToken = default);

    /// <summary>Finds an upload that is not yet committed or aborted.</summary>
    /// <param name="uploadId">Id of the upload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The upload, or null.</returns>
    Task<UploadRecord?> FindUploadAsync(string uploadId, CancellationToken cancellationToken = default);

    /// <summary>Finds a user with credentials by login name.</summary>
    /// <param name="username">Login name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The user, or null.</returns>
    Task<UserCredentials?> FindUserByNameAsync(string username, CancellationToken cancellationToken = default);

    /// <summary>Finds a user with credentials by id.</summary>
    /// <param name="userId">Id of the user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The user, or null.</returns>
    Task<UserCredentials?> FindUserByIdAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Lists all users.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The users.</returns>
    Task<IReadOnlyList<UserInfo>> ListUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists all groups.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The groups.</returns>
    Task<IReadOnlyList<GroupInfo>> ListGroupsAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists all roles, built-in ones included.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The roles.</returns>
    Task<IReadOnlyList<RoleInfo>> ListRolesAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists grants, optionally filtered.</summary>
    /// <param name="itemId">Only grants on this item; null with <paramref name="systemOnly"/> false means any.</param>
    /// <param name="subjectId">Only grants of this user or group.</param>
    /// <param name="systemOnly">True to return only system grants.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The grants.</returns>
    Task<IReadOnlyList<GrantInfo>> ListGrantsAsync(string? itemId, string? subjectId, bool systemOnly, CancellationToken cancellationToken = default);

    /// <summary>Finds a session by token.</summary>
    /// <param name="token">The session token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The session, or null.</returns>
    Task<SessionRecord?> FindSessionAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Lists the registered Data Servers.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The Data Servers.</returns>
    Task<IReadOnlyList<DataServerInfo>> ListDataServersAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists audit entries, newest first.</summary>
    /// <param name="from">Inclusive lower time bound.</param>
    /// <param name="to">Inclusive upper time bound.</param>
    /// <param name="userId">Only entries of this user.</param>
    /// <param name="limit">Maximum number of entries.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The entries.</returns>
    Task<IReadOnlyList<AuditEntry>> ListAuditAsync(DateTime? from, DateTime? to, string? userId, int limit, CancellationToken cancellationToken = default);
}
