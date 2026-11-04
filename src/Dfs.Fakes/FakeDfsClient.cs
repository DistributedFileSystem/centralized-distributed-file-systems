using System.Collections.Concurrent;
using Dfs.Shared;

namespace Dfs.Fakes;

public class FakeDfsClient : IDfsClient
{
    private readonly List<DfsUser> _users = [];
    private readonly List<DfsGroup> _groups = [];
    private readonly List<DfsGrant> _grants = [];
    private readonly List<DfsItem> _items = [];
    private readonly ConcurrentDictionary<string, List<FileVersionInfo>> _versions = new();
    private readonly List<TransferProgress> _transfers = [];
    private readonly List<ClusterNodeInfo> _nodes = [];

    public bool IsConnected { get; private set; } = true;
    public string ServerEndpoint { get; private set; } = "192.168.10.11:7000";
    public string? SessionToken { get; private set; }
    public UserInfo? CurrentUser { get; private set; }

    public FakeDfsClient()
    {
        InitializeSampleData();
    }

    private void InitializeSampleData()
    {
        // 1. Users
        _users.AddRange([
            new DfsUser("u-1", "admin", "System Administrator", false, DateTime.UtcNow.AddDays(-3), ["Admin", "Team A"]),
            new DfsUser("u-2", "an.nguyen", "Nguyễn Văn An", false, DateTime.UtcNow.AddDays(-2), ["Team A"]),
            new DfsUser("u-3", "tan.phan", "Phan Nhật Tân", false, DateTime.UtcNow.AddDays(-2), ["Team A"]),
            new DfsUser("u-4", "cuong.khong", "Khổng Phi Cường", false, DateTime.UtcNow.AddDays(-2), ["Team A"]),
            new DfsUser("u-5", "guest_user", "Khách Thử Nghiệm", true, DateTime.UtcNow.AddDays(-1), ["Guests"])
        ]);

        // Default login as an.nguyen
        CurrentUser = new UserInfo("u-2", "an.nguyen", "Nguyễn Văn An", ["Editor"], ["file.read", "file.write", "item.create", "item.rename", "item.delete"]);
        SessionToken = "sess_fake_token_01H8X9A77B";

        // 2. Groups
        _groups.AddRange([
            new DfsGroup("g-1", "Team A", ["an.nguyen", "tan.phan", "cuong.khong"]),
            new DfsGroup("g-2", "Guests", ["guest_user"]),
            new DfsGroup("g-3", "Auditors", ["cuong.khong"])
        ]);

        // 3. File System Items
        var now = DateTime.Now;
        _items.AddRange([
            new DfsItem("root", null, "Root", "directory", 0, "admin", 1, now.AddDays(-3)),
            new DfsItem("dir-proj", "root", "projects", "directory", 0, "admin", 1, now.AddDays(-2)),
            new DfsItem("dir-dfs", "dir-proj", "dfs", "directory", 0, "an.nguyen", 1, now.AddHours(-4)),
            new DfsItem("dir-ai", "dir-proj", "ai_lab", "directory", 0, "tan.phan", 1, now.AddDays(-1)),
            new DfsItem("dir-shared", "root", "shared", "directory", 0, "admin", 1, now.AddDays(-2)),
            new DfsItem("dir-users", "root", "users", "directory", 0, "admin", 1, now.AddDays(-2)),
            new DfsItem("dir-user-an", "dir-users", "an.nguyen", "directory", 0, "an.nguyen", 1, now.AddHours(-12)),

            // Files in /projects/dfs
            new DfsItem("file-rep", "dir-dfs", "report_final.pdf", "file", 14_889_796, "an.nguyen", 3, now.AddMinutes(-40)),
            new DfsItem("file-arch", "dir-dfs", "architecture.png", "file", 2_936_012, "nhat.le", 1, now.AddHours(-7)),
            new DfsItem("file-data", "dir-dfs", "data_test_50mb.bin", "file", 52_428_800, "cuong.khong", 1, now.AddHours(-5)),
            new DfsItem("file-code", "dir-dfs", "source_archive.zip", "file", 8_450_000, "tan.phan", 2, now.AddHours(-1)),

            // Files in /shared
            new DfsItem("file-zip", "dir-shared", "big_dataset_archive.zip", "file", 52_428_800, "tan.phan", 1, now.AddDays(-1)),
            new DfsItem("file-txt", "dir-shared", "welcome.txt", "file", 1_228, "admin", 1, now.AddDays(-2))
        ]);

        // 4. File Versions
        _versions["file-rep"] = [
            new FileVersionInfo("file-rep", 3, 14_889_796, 4, "an.nguyen", now.AddMinutes(-40)),
            new FileVersionInfo("file-rep", 2, 12_582_912, 3, "tan.phan", now.AddHours(-2)),
            new FileVersionInfo("file-rep", 1, 10_276_044, 3, "an.nguyen", now.AddDays(-1))
        ];

        // 5. Grants on /projects
        _grants.AddRange([
            new DfsGrant("g-01", "/projects", "User", "an.nguyen", "Owner", false),
            new DfsGrant("g-02", "/projects", "Group", "Team A", "Editor", false),
            new DfsGrant("g-03", "/projects", "Group", "Guests", "Viewer", false),
            new DfsGrant("g-04", "/projects/dfs", "Group", "Team A", "Editor", true)
        ]);

        // 6. Cluster Nodes (5 Laptops in LAN)
        _nodes.AddRange([
            new ClusterNodeInfo("ds-1", "DataServer", "Laptop 1 (Tân)", "192.168.10.11:7001", "Healthy", 78_500_000_000, 100_000_000_000, 6_420, DateTime.UtcNow.AddSeconds(-1)),
            new ClusterNodeInfo("ds-2", "DataServer", "Laptop 2 (Nhất)", "192.168.10.12:7001", "Healthy", 72_000_000_000, 100_000_000_000, 6_850, DateTime.UtcNow.AddSeconds(-2)),
            new ClusterNodeInfo("ds-3", "DataServer", "Laptop 3 (Cường)", "192.168.10.13:7001", "Healthy", 81_200_000_000, 100_000_000_000, 6_100, DateTime.UtcNow.AddSeconds(-1)),
            new ClusterNodeInfo("ds-4", "DataServer", "Laptop 4 (Nguyên)", "192.168.10.14:7001", "Healthy", 76_400_000_000, 100_000_000_000, 6_580, DateTime.UtcNow.AddSeconds(-1)),
            new ClusterNodeInfo("ds-5", "DataServer", "Laptop 5 (Quân)", "192.168.10.15:7001", "Dead", 0, 100_000_000_000, 6_500, DateTime.UtcNow.AddSeconds(-25))
        ]);

        // 7. Sample Transfers
        _transfers.AddRange([
            new TransferProgress(
                "t-1",
                "report_final_v4.pdf",
                "/projects/dfs/report_final_v4.pdf",
                "Upload",
                7_000_000,
                10_000_000,
                4.2,
                70,
                "Đang truyền các khối dữ liệu...",
                [
                    new ChunkLocation(0, "c-01", "192.168.10.11:7001", 4_194_304, "e3b0c442...", "100% OK"),
                    new ChunkLocation(1, "c-02", "192.168.10.13:7001", 4_194_304, "8f434346...", "75%"),
                    new ChunkLocation(2, "c-03", "192.168.10.15:7001", 1_611_392, "ca978112...", "35%")
                ]
            ),
            new TransferProgress(
                "t-2",
                "big_dataset_archive.zip",
                "/shared/big_dataset_archive.zip",
                "Download",
                16_000_000,
                50_000_000,
                8.3,
                32,
                "Đang đọc song song từ 5 Data Servers"
            ),
            new TransferProgress(
                "t-3",
                "presentation_slides.pptx",
                "/projects/dfs/presentation_slides.pptx",
                "Upload",
                4_500_000,
                4_500_000,
                0,
                100,
                "Đã hoàn tất (SHA-256 Verified)"
            )
        ]);
    }

    public Task<LoginResult> LoginAsync(string username, string password, string endpoint, CancellationToken cancellationToken = default)
    {
        ServerEndpoint = endpoint;
        var user = _users.FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
        if (user == null)
        {
            return Task.FromResult(new LoginResult(false, null, null, "Sai tên đăng nhập hoặc mật khẩu không đúng."));
        }

        if (user.IsDisabled)
        {
            return Task.FromResult(new LoginResult(false, null, null, "Tài khoản của bạn đã bị Quản trị viên vô hiệu hóa."));
        }

        SessionToken = $"sess_{Guid.NewGuid():N}";
        var roles = user.Username.Equals("admin", StringComparison.OrdinalIgnoreCase)
            ? new List<string> { "Admin", "Editor", "Viewer" }
            : new List<string> { "Editor", "Viewer" };

        CurrentUser = new UserInfo(user.Id, user.Username, user.FullName, roles, ["file.read", "file.write", "item.create", "item.rename", "item.delete"]);
        return Task.FromResult(new LoginResult(true, SessionToken, CurrentUser, null));
    }

    public Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        SessionToken = null;
        CurrentUser = null;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DfsItem>> ListDirAsync(string path, CancellationToken cancellationToken = default)
    {
        string? targetParentId = "root";
        if (path == "/" || string.IsNullOrWhiteSpace(path))
        {
            targetParentId = "root";
        }
        else
        {
            var clean = path.Trim('/');
            var segments = clean.Split('/');
            string currentParent = "root";
            foreach (var seg in segments)
            {
                var match = _items.FirstOrDefault(i => i.ParentId == currentParent && i.IsDirectory && i.Name.Equals(seg, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    currentParent = match.Id;
                }
            }
            targetParentId = currentParent;
        }

        var results = _items.Where(i => i.ParentId == targetParentId).ToList();
        return Task.FromResult<IReadOnlyList<DfsItem>>(results);
    }

    public Task<DfsItem?> StatAsync(string path, CancellationToken cancellationToken = default)
    {
        var clean = path.Trim('/');
        var name = Path.GetFileName(clean);
        var item = _items.FirstOrDefault(i => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(item);
    }

    public Task CreateFolderAsync(string path, CancellationToken cancellationToken = default)
    {
        var clean = path.Trim('/');
        var parts = clean.Split('/');
        var name = parts[^1];
        var parentDirName = parts.Length > 1 ? parts[^2] : "root";
        var parent = _items.FirstOrDefault(i => i.IsDirectory && i.Name.Equals(parentDirName, StringComparison.OrdinalIgnoreCase))
                     ?? _items.First(i => i.Id == "root");

        var newDir = new DfsItem($"dir_{Guid.NewGuid():N}", parent.Id, name, "directory", 0, CurrentUser?.Username ?? "an.nguyen", 1, DateTime.Now);
        _items.Add(newDir);
        return Task.CompletedTask;
    }

    public Task RenameAsync(string oldPath, string newPath, CancellationToken cancellationToken = default)
    {
        var oldName = Path.GetFileName(oldPath.Trim('/'));
        var newName = Path.GetFileName(newPath.Trim('/'));
        var item = _items.FirstOrDefault(i => i.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase));
        if (item != null)
        {
            _items.Remove(item);
            _items.Add(item with { Name = newName, UpdatedAt = DateTime.Now });
        }
        return Task.CompletedTask;
    }

    public Task MoveAsync(string sourcePath, string targetDirectory, CancellationToken cancellationToken = default)
    {
        var srcName = Path.GetFileName(sourcePath.Trim('/'));
        var targetDirName = Path.GetFileName(targetDirectory.Trim('/'));
        var item = _items.FirstOrDefault(i => i.Name.Equals(srcName, StringComparison.OrdinalIgnoreCase));
        var targetDir = _items.FirstOrDefault(i => i.IsDirectory && i.Name.Equals(targetDirName, StringComparison.OrdinalIgnoreCase));

        if (item != null && targetDir != null)
        {
            _items.Remove(item);
            _items.Add(item with { ParentId = targetDir.Id, UpdatedAt = DateTime.Now });
        }
        return Task.CompletedTask;
    }

    public Task DeleteItemAsync(string path, CancellationToken cancellationToken = default)
    {
        var name = Path.GetFileName(path.Trim('/'));
        var item = _items.FirstOrDefault(i => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (item != null)
        {
            _items.Remove(item);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<FileVersionInfo>> ListVersionsAsync(string path, CancellationToken cancellationToken = default)
    {
        var name = Path.GetFileName(path.Trim('/'));
        var item = _items.FirstOrDefault(i => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (item != null && _versions.TryGetValue(item.Id, out var vers))
        {
            return Task.FromResult<IReadOnlyList<FileVersionInfo>>(vers);
        }

        // Return a default mock version if not found
        IReadOnlyList<FileVersionInfo> fallback = [
            new FileVersionInfo(item?.Id ?? "f-1", 1, item?.Size ?? 1024, 1, item?.Owner ?? "an.nguyen", DateTime.Now)
        ];
        return Task.FromResult(fallback);
    }

    public Task<TransferProgress> UploadFileAsync(string localPath, string remotePath, int? baseVersion = null, IProgress<TransferProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var fileName = Path.GetFileName(localPath);
        var transfer = new TransferProgress(
            $"t_{Guid.NewGuid():N}",
            fileName,
            remotePath,
            "Upload",
            10_000_000,
            10_000_000,
            6.5,
            100,
            "Đã hoàn tất (SHA-256 Verified)"
        );
        _transfers.Insert(0, transfer);
        progress?.Report(transfer);
        return Task.FromResult(transfer);
    }

    public Task<TransferProgress> DownloadFileAsync(string remotePath, string localPath, int? version = null, IProgress<TransferProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var fileName = Path.GetFileName(remotePath);
        var transfer = new TransferProgress(
            $"t_{Guid.NewGuid():N}",
            fileName,
            remotePath,
            "Download",
            14_889_796,
            14_889_796,
            8.1,
            100,
            "Đã hoàn tất"
        );
        _transfers.Insert(0, transfer);
        progress?.Report(transfer);
        return Task.FromResult(transfer);
    }

    public Task<IReadOnlyList<TransferProgress>> GetTransfersAsync()
    {
        return Task.FromResult<IReadOnlyList<TransferProgress>>(_transfers);
    }

    public Task CancelTransferAsync(string taskId)
    {
        var item = _transfers.FirstOrDefault(t => t.TaskId == taskId);
        if (item != null)
        {
            _transfers.Remove(item);
            _transfers.Add(item with { Status = "Đã hủy bởi người dùng" });
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DfsUser>> ListUsersAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<DfsUser>>(_users);
    }

    public Task CreateUserAsync(string username, string initialPassword, string fullName, string group, CancellationToken cancellationToken = default)
    {
        var newUser = new DfsUser($"u_{Guid.NewGuid():N}", username, fullName, false, DateTime.UtcNow, [group]);
        _users.Add(newUser);
        return Task.CompletedTask;
    }

    public Task ToggleDisableUserAsync(string username, CancellationToken cancellationToken = default)
    {
        var user = _users.FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
        if (user != null)
        {
            _users.Remove(user);
            _users.Add(user with { IsDisabled = !user.IsDisabled });
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DfsGroup>> ListGroupsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<DfsGroup>>(_groups);
    }

    public Task CreateGroupAsync(string name, CancellationToken cancellationToken = default)
    {
        _groups.Add(new DfsGroup($"g_{Guid.NewGuid():N}", name, []));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DfsGrant>> ListGrantsAsync(string path, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<DfsGrant>>(_grants);
    }

    public Task SetGrantAsync(string path, string subjectType, string subjectId, string role, CancellationToken cancellationToken = default)
    {
        _grants.Add(new DfsGrant($"grant_{Guid.NewGuid():N}", path, subjectType, subjectId, role, false));
        return Task.CompletedTask;
    }

    public Task RevokeGrantAsync(string grantId, CancellationToken cancellationToken = default)
    {
        var grant = _grants.FirstOrDefault(g => g.Id == grantId);
        if (grant != null)
        {
            _grants.Remove(grant);
        }
        return Task.CompletedTask;
    }

    public Task<ClusterStatusInfo> GetClusterStatusAsync(CancellationToken cancellationToken = default)
    {
        long totalCapacity = _nodes.Sum(n => n.TotalBytes);
        long totalFree = _nodes.Sum(n => n.FreeBytes);
        int totalChunks = _nodes.Sum(n => n.ChunkCount);
        var info = new ClusterStatusInfo(_nodes, totalCapacity, totalFree, totalChunks, "Laptop 1 (Tân - 192.168.10.11:7000)", 142);
        return Task.FromResult(info);
    }
}
