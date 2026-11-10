using System;
using Dfs.Shared.Messages;

namespace Dfs.Client.Desktop.Models;

public sealed class UiFileItem
{
    public ItemInfo Raw { get; }
    public string Name => Raw.Name;
    public string Path => Raw.Path;
    public bool IsFolder => Raw.Kind == ItemKind.Folder;
    public string IconGlyph => IsFolder ? "📁" : "📄";
    public string SizeString => IsFolder ? "-" : FormatBytes(Raw.Size);
    public string VersionString => IsFolder ? "-" : $"v{Raw.CurrentVersion}";
    public string OwnerString => Raw.OwnerId;
    public string ModifiedString => Raw.ModifiedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public UiFileItem(ItemInfo raw)
    {
        Raw = raw;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F2} MB";
    }
}

public sealed class UiTransferItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string FileName { get; set; } = "";
    public string Direction { get; set; } = "Upload";
    public int Progress { get; set; } = 0;
    public string Status { get; set; } = "Đang truyền";
    public string TargetServers { get; set; } = "127.0.0.1:7001";
    public string Speed { get; set; } = "4.2 MB/s";
    public string FormattedProgress => $"{Progress}%";
}

public sealed class UiNodeItem
{
    public DataServerInfo Raw { get; }
    public string NodeId => Raw.NodeId;
    public string Address => Raw.Address;
    public string StatusText => Raw.Status switch
    {
        DataServerStatus.Alive => "Healthy (Online)",
        DataServerStatus.Dead => "Dead (Offline)",
        _ => "Unknown"
    };
    public string StatusColor => Raw.Status switch
    {
        DataServerStatus.Alive => "#28A745",
        _ => "#DC3545"
    };
    public string FormattedCapacity => FormatBytes(Raw.CapacityBytes);
    public string FormattedFree => FormatBytes(Raw.FreeBytes);
    public string FormattedChunks => $"{Raw.ChunkCount:N0} chunks";
    public string LastHeartbeatString => Raw.LastHeartbeat.ToLocalTime().ToString("HH:mm:ss");

    public UiNodeItem(DataServerInfo raw)
    {
        Raw = raw;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F2} MB";
    }
}

public sealed class UiUserItem
{
    public string Id { get; }
    public string Username { get; }
    public bool Disabled { get; }
    public string StatusText => Disabled ? "Đã khóa" : "Hoạt động";
    public string StatusColor => Disabled ? "#DC3545" : "#28A745";
    public string GroupsText { get; }

    public UiUserItem(UserInfo raw)
    {
        Id = raw.Id;
        Username = raw.Username;
        Disabled = raw.Disabled;
        GroupsText = raw.GroupIds.Count > 0 ? string.Join(", ", raw.GroupIds) : "Không có";
    }

    public UiUserItem(string id, string username, bool disabled, string groups)
    {
        Id = id;
        Username = username;
        Disabled = disabled;
        GroupsText = groups;
    }
}

public sealed class UiGroupItem
{
    public string Id { get; }
    public string Name { get; }
    public string MemberCountText { get; }

    public UiGroupItem(GroupInfo raw)
    {
        Id = raw.Id;
        Name = raw.Name;
        MemberCountText = $"{raw.MemberIds.Count} thành viên";
    }

    public UiGroupItem(string id, string name, int count)
    {
        Id = id;
        Name = name;
        MemberCountText = $"{count} thành viên";
    }
}

public sealed class UiGrantItem
{
    public string Id { get; }
    public string RoleId { get; }
    public string Subject { get; }
    public string TargetPath { get; }
    public string InheritText { get; }

    public UiGrantItem(GrantInfo raw)
    {
        Id = raw.Id;
        RoleId = raw.RoleId;
        Subject = $"{raw.SubjectType}: {raw.SubjectId}";
        TargetPath = raw.Path ?? "(Hệ thống toàn cục)";
        InheritText = "Kế thừa";
    }

    public UiGrantItem(string id, string roleId, string subject, string path, bool inherit)
    {
        Id = id;
        RoleId = roleId;
        Subject = subject;
        TargetPath = path;
        InheritText = inherit ? "Có" : "Không";
    }
}
