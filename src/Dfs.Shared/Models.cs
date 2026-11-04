namespace Dfs.Shared;

public record DfsItem(
    string Id,
    string? ParentId,
    string Name,
    string Kind, // "directory" or "file"
    long Size,
    string Owner,
    int CurrentVersion,
    DateTime UpdatedAt)
{
    public bool IsDirectory => Kind == "directory";
    public bool IsFile => Kind == "file";

    public string FormattedSize
    {
        get
        {
            if (IsDirectory) return "--";
            if (Size < 1024) return $"{Size} B";
            if (Size < 1024 * 1024) return $"{Size / 1024.0:F1} KB";
            if (Size < 1024 * 1024 * 1024) return $"{Size / (1024.0 * 1024.0):F1} MB";
            return $"{Size / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }
    }

    public string FormattedVersion => IsDirectory ? "--" : $"v{CurrentVersion}";
    public string FormattedDate => UpdatedAt.ToString("dd/MM/yyyy HH:mm");
    public string Icon => IsDirectory ? "📁" : GetFileIcon(Name);

    private static string GetFileIcon(string name)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "📄",
            ".png" or ".jpg" or ".jpeg" or ".svg" => "🖼️",
            ".zip" or ".tar" or ".gz" or ".rar" => "📦",
            ".txt" or ".md" => "📝",
            ".bin" or ".iso" => "💾",
            ".doc" or ".docx" => "📘",
            ".xls" or ".xlsx" => "📊",
            _ => "📄"
        };
    }
}

public record UserInfo(
    string Id,
    string Username,
    string FullName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions)
{
    public bool IsAdmin => Roles.Contains("Admin", StringComparer.OrdinalIgnoreCase);
    public bool IsAuditor => Roles.Contains("Auditor", StringComparer.OrdinalIgnoreCase);
}

public record LoginResult(
    bool Success,
    string? SessionToken,
    UserInfo? User,
    string? ErrorMessage);

public record ChunkLocation(
    int Index,
    string ChunkId,
    string ServerAddress,
    long Size,
    string Sha256,
    string Status);

public record TransferProgress(
    string TaskId,
    string FileName,
    string RemotePath,
    string Direction, // "Upload" or "Download"
    long TransferredBytes,
    long TotalBytes,
    double SpeedMbPerSec,
    int Percent,
    string Status,
    IReadOnlyList<ChunkLocation>? Chunks = null)
{
    public string FormattedProgress => $"{Percent}% ({FormattedTransferred} / {FormattedTotal})";
    public string FormattedSpeed => $"{SpeedMbPerSec:F1} MB/s";
    public string FormattedTransferred => FormatBytes(TransferredBytes);
    public string FormattedTotal => FormatBytes(TotalBytes);

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }
}

public record FileVersionInfo(
    string ItemId,
    int Version,
    long Size,
    int ChunkCount,
    string CreatedBy,
    DateTime CreatedAt)
{
    public string FormattedVersion => $"v{Version}";
    public string FormattedSize => Size < 1024 * 1024 ? $"{Size / 1024.0:F1} KB" : $"{Size / (1024.0 * 1024.0):F1} MB";
    public string FormattedDate => CreatedAt.ToString("dd/MM/yyyy HH:mm");
}

public record ClusterNodeInfo(
    string NodeId,
    string Role,
    string MemberName,
    string Address,
    string Status, // "Healthy" or "Dead"
    long FreeBytes,
    long TotalBytes,
    int ChunkCount,
    DateTime LastHeartbeat)
{
    public bool IsHealthy => Status.Equals("Healthy", StringComparison.OrdinalIgnoreCase);
    public string FormattedStatus => IsHealthy ? "🟢 Hoạt động" : "🔴 Mất kết nối";
    public string FormattedCapacity => $"{FreeBytes / (1024.0 * 1024.0 * 1024.0):F1} GB trống / {TotalBytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
    public string FormattedChunkCount => $"{ChunkCount:N0} chunks";
    public int UsagePercent => TotalBytes > 0 ? (int)((TotalBytes - FreeBytes) * 100 / TotalBytes) : 0;
    public string LastSeenSeconds => $"{(int)(DateTime.UtcNow - LastHeartbeat).TotalSeconds}s trước";
}

public record ClusterStatusInfo(
    IReadOnlyList<ClusterNodeInfo> Nodes,
    long TotalCapacityBytes,
    long TotalFreeBytes,
    int TotalChunks,
    string MetadataLeader,
    int OpLogSeq);

public record DfsUser(
    string Id,
    string Username,
    string FullName,
    bool IsDisabled,
    DateTime CreatedAt,
    IReadOnlyList<string> Groups)
{
    public string StatusText => IsDisabled ? "Đã khóa" : "Hoạt động";
    public string GroupListText => string.Join(", ", Groups);
}

public record DfsGroup(
    string Id,
    string Name,
    IReadOnlyList<string> Members);

public record DfsGrant(
    string Id,
    string Path,
    string SubjectType, // "User" or "Group"
    string SubjectId,
    string Role,
    bool IsInherited);
