using Dfs.Shared.Messages;

namespace Dfs.Shared.Client;

/// <summary>Client SDK used by the desktop app. It keeps the session after <see cref="LoginAsync"/>, talks to the Metadata Server and Data Servers, and throws <see cref="DfsException"/> carrying the <see cref="ErrorCode"/> when the server answers with an error.</summary>
public interface IDfsClient
{
    /// <summary>Logs in and keeps the session for later calls.</summary>
    /// <param name="username">Login name.</param>
    /// <param name="password">Plain password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The session, user, roles and permissions.</returns>
    Task<LoginResponse> LoginAsync(string username, string password, CancellationToken cancellationToken = default);

    /// <summary>Logs out and drops the session.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task LogoutAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists the children of a folder.</summary>
    /// <param name="path">Absolute path of the folder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The children the user may list.</returns>
    Task<IReadOnlyList<ItemInfo>> ListDirAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Gets one item.</summary>
    /// <param name="path">Absolute path of the item.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The item.</returns>
    Task<ItemInfo> StatAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Lists retained versions of a file.</summary>
    /// <param name="path">Absolute path of the file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The versions, newest first.</returns>
    Task<IReadOnlyList<VersionInfo>> ListVersionsAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Creates a folder.</summary>
    /// <param name="path">Absolute path of the new folder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new folder.</returns>
    Task<ItemInfo> CreateFolderAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Renames an item.</summary>
    /// <param name="path">Absolute path of the item.</param>
    /// <param name="newName">New name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The renamed item.</returns>
    Task<ItemInfo> RenameAsync(string path, string newName, CancellationToken cancellationToken = default);

    /// <summary>Moves an item to another folder.</summary>
    /// <param name="path">Absolute path of the item.</param>
    /// <param name="newParentPath">Absolute path of the destination folder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The moved item.</returns>
    Task<ItemInfo> MoveAsync(string path, string newParentPath, CancellationToken cancellationToken = default);

    /// <summary>Deletes a file or a folder tree.</summary>
    /// <param name="path">Absolute path of the item.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteItemAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Uploads content as a new file or a new version: CreateUpload, WriteChunk for each chunk, CommitUpload.</summary>
    /// <param name="path">Absolute path of the file.</param>
    /// <param name="content">Content to upload.</param>
    /// <param name="baseVersion">Version the change is based on; 0 for a new file. A stale value throws <see cref="ErrorCode.Conflict"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The committed file and its new version.</returns>
    Task<CommitUploadResponse> UploadFileAsync(string path, Stream content, int baseVersion, CancellationToken cancellationToken = default);

    /// <summary>Downloads a version of a file: Open, ReadChunk for each chunk, checksum verification.</summary>
    /// <param name="path">Absolute path of the file.</param>
    /// <param name="destination">Stream that receives the content.</param>
    /// <param name="version">Version to download; 0 means the current version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DownloadFileAsync(string path, Stream destination, int version = 0, CancellationToken cancellationToken = default);

    /// <summary>Creates a user.</summary>
    /// <param name="username">Login name.</param>
    /// <param name="password">Plain password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new user.</returns>
    Task<UserInfo> CreateUserAsync(string username, string password, CancellationToken cancellationToken = default);

    /// <summary>Lists users.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All users.</returns>
    Task<IReadOnlyList<UserInfo>> ListUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>Disables or enables a user.</summary>
    /// <param name="userId">Id of the user.</param>
    /// <param name="disabled">True to disable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SetUserDisabledAsync(string userId, bool disabled, CancellationToken cancellationToken = default);

    /// <summary>Resets the password of a user.</summary>
    /// <param name="userId">Id of the user.</param>
    /// <param name="newPassword">The new plain password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ResetPasswordAsync(string userId, string newPassword, CancellationToken cancellationToken = default);

    /// <summary>Creates a group.</summary>
    /// <param name="name">Name of the group.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new group.</returns>
    Task<GroupInfo> CreateGroupAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Lists groups.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All groups.</returns>
    Task<IReadOnlyList<GroupInfo>> ListGroupsAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds or removes a group member.</summary>
    /// <param name="groupId">Id of the group.</param>
    /// <param name="userId">Id of the user.</param>
    /// <param name="member">True to add, false to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SetGroupMemberAsync(string groupId, string userId, bool member, CancellationToken cancellationToken = default);

    /// <summary>Creates a role.</summary>
    /// <param name="name">Name of the role.</param>
    /// <param name="scope">System or item scope.</param>
    /// <param name="permissions">Permission names.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new role.</returns>
    Task<RoleInfo> CreateRoleAsync(string name, RoleScope scope, IReadOnlyList<string> permissions, CancellationToken cancellationToken = default);

    /// <summary>Lists roles.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All roles.</returns>
    Task<IReadOnlyList<RoleInfo>> ListRolesAsync(CancellationToken cancellationToken = default);

    /// <summary>Replaces the permissions of a role.</summary>
    /// <param name="roleId">Id of the role.</param>
    /// <param name="permissions">The new permission names.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated role.</returns>
    Task<RoleInfo> UpdateRoleAsync(string roleId, IReadOnlyList<string> permissions, CancellationToken cancellationToken = default);

    /// <summary>Grants a role to a user or group on a path, or system wide when <paramref name="path"/> is null.</summary>
    /// <param name="roleId">Id of the role.</param>
    /// <param name="subjectType">User or group.</param>
    /// <param name="subjectId">Id of the user or group.</param>
    /// <param name="path">Absolute path; null for a system grant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new grant.</returns>
    Task<GrantInfo> CreateGrantAsync(string roleId, SubjectType subjectType, string subjectId, string? path = null, CancellationToken cancellationToken = default);

    /// <summary>Lists grants on a path, or system grants when <paramref name="path"/> is null.</summary>
    /// <param name="path">Absolute path; null for system grants.</param>
    /// <param name="subjectId">Only grants of this user or group.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching grants.</returns>
    Task<IReadOnlyList<GrantInfo>> ListGrantsAsync(string? path = null, string? subjectId = null, CancellationToken cancellationToken = default);

    /// <summary>Revokes a grant.</summary>
    /// <param name="grantId">Id of the grant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RevokeGrantAsync(string grantId, CancellationToken cancellationToken = default);

    /// <summary>Lists audit entries.</summary>
    /// <param name="from">Inclusive lower time bound.</param>
    /// <param name="to">Inclusive upper time bound.</param>
    /// <param name="userId">Only entries of this user.</param>
    /// <param name="limit">Maximum number of entries (default 100, max 1000).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching entries.</returns>
    Task<IReadOnlyList<AuditEntry>> ListAuditAsync(DateTime? from = null, DateTime? to = null, string? userId = null, int limit = 100, CancellationToken cancellationToken = default);

    /// <summary>Lists Data Servers with their state.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All registered Data Servers.</returns>
    Task<IReadOnlyList<DataServerInfo>> ClusterStatusAsync(CancellationToken cancellationToken = default);
}
