using Dfs.Shared.Client;
using Dfs.Shared.Messages;
using Dfs.Shared.Security;

namespace Dfs.Fakes;

/// <summary>In-memory client for UI work. Sign in with demo/demo; the sample tree starts at /projects.</summary>
public sealed class FakeDfsClient : IDfsClient
{
    private bool _loggedIn;

    public Task<LoginResponse> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (username != "demo" || password != "demo")
            throw new DfsException(ErrorCode.Unauthorized, "Invalid demo credentials.");

        _loggedIn = true;
        return Task.FromResult(new LoginResponse(Guid.NewGuid().ToString(), "s-demo", DateTime.UtcNow.AddHours(8),
            new UserRef(FakeSamples.UserId, username), new ValueList<string>([BuiltInRoles.Admin]),
            new ValueList<string>([Permission.UserManage, Permission.GroupManage, Permission.RoleManage,
                Permission.ClusterView, Permission.ClusterManage, Permission.AuditView])));
    }

    public Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _loggedIn = false;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ItemInfo>> ListDirAsync(string path, CancellationToken cancellationToken = default)
    {
        RequireLogin(cancellationToken);
        var folder = Find(path);
        if (folder.Kind != ItemKind.Folder)
            throw new DfsException(ErrorCode.BadRequest, "Path is not a folder.");

        var prefix = path == "/" ? "/" : path + "/";
        IReadOnlyList<ItemInfo> children = FakeSamples.Items
            .Where(item => item.Path != path && item.Path.StartsWith(prefix, StringComparison.Ordinal)
                && !item.Path[prefix.Length..].Contains('/'))
            .ToArray();
        return Task.FromResult(children);
    }

    public Task<ItemInfo> StatAsync(string path, CancellationToken cancellationToken = default)
    {
        RequireLogin(cancellationToken);
        return Task.FromResult(Find(path));
    }

    public Task<IReadOnlyList<VersionInfo>> ListVersionsAsync(string path, CancellationToken cancellationToken = default)
    {
        RequireLogin(cancellationToken);
        if (Find(path).Kind != ItemKind.File)
            throw new DfsException(ErrorCode.BadRequest, "Path is not a file.");
        return Task.FromResult<IReadOnlyList<VersionInfo>>([FakeSamples.Version]);
    }

    public Task<IReadOnlyList<DataServerInfo>> ClusterStatusAsync(CancellationToken cancellationToken = default)
    {
        RequireLogin(cancellationToken);
        return Task.FromResult<IReadOnlyList<DataServerInfo>>(
            [new("d-demo", "127.0.0.1:7001", DataServerStatus.Alive, 1024 * 1024, 1024 * 1024 - FakeSamples.Content.Length, 1, DateTime.UtcNow)]);
    }

    private static ItemInfo Find(string path) => FakeSamples.Items.FirstOrDefault(item => item.Path == path)
        ?? throw new DfsException(ErrorCode.NotFound, $"No demo item at {path}.");

    private void RequireLogin(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_loggedIn) throw new DfsException(ErrorCode.Unauthorized, "Log in to the demo first.");
    }

    private static NotSupportedException Unsupported() => new("This operation is not implemented by the demo client.");

    public Task<ItemInfo> CreateFolderAsync(string path, CancellationToken cancellationToken = default) => Task.FromException<ItemInfo>(Unsupported());
    public Task<ItemInfo> RenameAsync(string path, string newName, CancellationToken cancellationToken = default) => Task.FromException<ItemInfo>(Unsupported());
    public Task<ItemInfo> MoveAsync(string path, string newParentPath, CancellationToken cancellationToken = default) => Task.FromException<ItemInfo>(Unsupported());
    public Task DeleteItemAsync(string path, CancellationToken cancellationToken = default) => Task.FromException(Unsupported());
    public Task<CommitUploadResponse> UploadFileAsync(string path, Stream content, int baseVersion, CancellationToken cancellationToken = default) => Task.FromException<CommitUploadResponse>(Unsupported());
    public Task DownloadFileAsync(string path, Stream destination, int version = 0, CancellationToken cancellationToken = default) => Task.FromException(Unsupported());
    public Task<UserInfo> CreateUserAsync(string username, string password, CancellationToken cancellationToken = default) => Task.FromException<UserInfo>(Unsupported());
    public Task<IReadOnlyList<UserInfo>> ListUsersAsync(CancellationToken cancellationToken = default) => Task.FromException<IReadOnlyList<UserInfo>>(Unsupported());
    public Task SetUserDisabledAsync(string userId, bool disabled, CancellationToken cancellationToken = default) => Task.FromException(Unsupported());
    public Task ResetPasswordAsync(string userId, string newPassword, CancellationToken cancellationToken = default) => Task.FromException(Unsupported());
    public Task<GroupInfo> CreateGroupAsync(string name, CancellationToken cancellationToken = default) => Task.FromException<GroupInfo>(Unsupported());
    public Task<IReadOnlyList<GroupInfo>> ListGroupsAsync(CancellationToken cancellationToken = default) => Task.FromException<IReadOnlyList<GroupInfo>>(Unsupported());
    public Task SetGroupMemberAsync(string groupId, string userId, bool member, CancellationToken cancellationToken = default) => Task.FromException(Unsupported());
    public Task<RoleInfo> CreateRoleAsync(string name, RoleScope scope, IReadOnlyList<string> permissions, CancellationToken cancellationToken = default) => Task.FromException<RoleInfo>(Unsupported());
    public Task<IReadOnlyList<RoleInfo>> ListRolesAsync(CancellationToken cancellationToken = default) => Task.FromException<IReadOnlyList<RoleInfo>>(Unsupported());
    public Task<RoleInfo> UpdateRoleAsync(string roleId, IReadOnlyList<string> permissions, CancellationToken cancellationToken = default) => Task.FromException<RoleInfo>(Unsupported());
    public Task<GrantInfo> CreateGrantAsync(string roleId, SubjectType subjectType, string subjectId, string? path = null, CancellationToken cancellationToken = default) => Task.FromException<GrantInfo>(Unsupported());
    public Task<IReadOnlyList<GrantInfo>> ListGrantsAsync(string? path = null, string? subjectId = null, CancellationToken cancellationToken = default) => Task.FromException<IReadOnlyList<GrantInfo>>(Unsupported());
    public Task RevokeGrantAsync(string grantId, CancellationToken cancellationToken = default) => Task.FromException(Unsupported());
    public Task<IReadOnlyList<AuditEntry>> ListAuditAsync(DateTime? from = null, DateTime? to = null, string? userId = null, int limit = 100, CancellationToken cancellationToken = default) => Task.FromException<IReadOnlyList<AuditEntry>>(Unsupported());
}
