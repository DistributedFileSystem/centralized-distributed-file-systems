namespace Dfs.Shared;

/// <summary>
/// Hợp đồng giao tiếp giữa giao diện người dùng Desktop (Avalonia) và hệ thống lưu trữ phân tán.
/// </summary>
public interface IDfsClient
{
    bool IsConnected { get; }
    string ServerEndpoint { get; }
    string? SessionToken { get; }
    UserInfo? CurrentUser { get; }

    // Authentication
    Task<LoginResult> LoginAsync(string username, string password, string endpoint, CancellationToken cancellationToken = default);
    Task LogoutAsync(CancellationToken cancellationToken = default);

    // Namespace operations
    Task<IReadOnlyList<DfsItem>> ListDirAsync(string path, CancellationToken cancellationToken = default);
    Task<DfsItem?> StatAsync(string path, CancellationToken cancellationToken = default);
    Task CreateFolderAsync(string path, CancellationToken cancellationToken = default);
    Task RenameAsync(string oldPath, string newPath, CancellationToken cancellationToken = default);
    Task MoveAsync(string sourcePath, string targetDirectory, CancellationToken cancellationToken = default);
    Task DeleteItemAsync(string path, CancellationToken cancellationToken = default);

    // Versions
    Task<IReadOnlyList<FileVersionInfo>> ListVersionsAsync(string path, CancellationToken cancellationToken = default);

    // Transfers
    Task<TransferProgress> UploadFileAsync(string localPath, string remotePath, int? baseVersion = null, IProgress<TransferProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<TransferProgress> DownloadFileAsync(string remotePath, string localPath, int? version = null, IProgress<TransferProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TransferProgress>> GetTransfersAsync();
    Task CancelTransferAsync(string taskId);

    // Admin: Users, Groups, Grants
    Task<IReadOnlyList<DfsUser>> ListUsersAsync(CancellationToken cancellationToken = default);
    Task CreateUserAsync(string username, string initialPassword, string fullName, string group, CancellationToken cancellationToken = default);
    Task ToggleDisableUserAsync(string username, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DfsGroup>> ListGroupsAsync(CancellationToken cancellationToken = default);
    Task CreateGroupAsync(string name, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DfsGrant>> ListGrantsAsync(string path, CancellationToken cancellationToken = default);
    Task SetGrantAsync(string path, string subjectType, string subjectId, string role, CancellationToken cancellationToken = default);
    Task RevokeGrantAsync(string grantId, CancellationToken cancellationToken = default);

    // Cluster Status
    Task<ClusterStatusInfo> GetClusterStatusAsync(CancellationToken cancellationToken = default);
}
