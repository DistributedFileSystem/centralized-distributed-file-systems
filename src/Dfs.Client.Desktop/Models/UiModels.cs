using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Dfs.Shared.Messages;

namespace Dfs.Client.Desktop.Models;

public sealed class UiDirectoryNode : INotifyPropertyChanged
{
    private bool _isExpanded = true;
    private bool _isSelected;

    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "/";
    public ObservableCollection<UiDirectoryNode> Children { get; } = [];

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public UiDirectoryNode(string name, string fullPath)
    {
        Name = name;
        FullPath = fullPath;
    }
}

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

public sealed class UiChunkItem : INotifyPropertyChanged
{
    private int _progress;
    private string _status = "Chờ truyền";

    public int ChunkIndex { get; set; }
    public string ChunkSize { get; set; } = "4.0 MB";
    public string DataServer { get; set; } = "";
    public string Sha256 { get; set; } = "";

    public int Progress
    {
        get => _progress;
        set
        {
            if (_progress != value)
            {
                _progress = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Progress)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FormattedProgress)));
            }
        }
    }

    public string Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
            }
        }
    }

    public string FormattedProgress => $"{Progress}%";
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class UiTransferItem : INotifyPropertyChanged
{
    private int _progress;
    private string _status = "Khởi tạo";
    private string _speed = "0.0 MB/s";
    private bool _isActive = true;
    private bool _isCompleted;
    private bool _isFailed;
    private bool _isCancelled;

    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string FileName { get; set; } = "";
    public string DestinationPath { get; set; } = "/";
    public string Direction { get; set; } = "Upload";
    public string TargetServers { get; set; } = "127.0.0.1:7001";
    public string SizeText { get; set; } = "0 B";
    public ObservableCollection<UiChunkItem> Chunks { get; } = [];

    public System.Threading.CancellationTokenSource? Cts { get; set; }

    public int Progress
    {
        get => _progress;
        set
        {
            if (_progress != value)
            {
                _progress = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Progress)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FormattedProgress)));
            }
        }
    }

    public string Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
            }
        }
    }

    public string Speed
    {
        get => _speed;
        set
        {
            if (_speed != value)
            {
                _speed = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Speed)));
            }
        }
    }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive != value)
            {
                _isActive = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanCancel)));
            }
        }
    }

    public bool IsCompleted
    {
        get => _isCompleted;
        set
        {
            if (_isCompleted != value)
            {
                _isCompleted = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCompleted)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProgressBarColor)));
            }
        }
    }

    public bool IsFailed
    {
        get => _isFailed;
        set
        {
            if (_isFailed != value)
            {
                _isFailed = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFailed)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRetry)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProgressBarColor)));
            }
        }
    }

    public bool IsCancelled
    {
        get => _isCancelled;
        set
        {
            if (_isCancelled != value)
            {
                _isCancelled = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCancelled)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRetry)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProgressBarColor)));
            }
        }
    }

    public bool CanCancel => IsActive;
    public bool CanRetry => IsFailed || IsCancelled;

    public string FormattedProgress => $"{Progress}%";

    public string ProgressBarColor =>
        IsCompleted ? "#28A745" :
        IsFailed ? "#DC3545" :
        IsCancelled ? "#6C757D" : "#0066CC";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Cancel()
    {
        if (!IsActive) return;
        Cts?.Cancel();
        IsActive = false;
        IsCancelled = true;
        Status = "Đã hủy bởi người dùng";
        Speed = "0 MB/s";
    }
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

public sealed class UiUserItem : INotifyPropertyChanged
{
    private bool _disabled;
    private string _groupsText = "";

    public string Id { get; }
    public string Username { get; }

    public bool Disabled
    {
        get => _disabled;
        set
        {
            if (_disabled != value)
            {
                _disabled = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Disabled)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusColor)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ToggleActionText)));
            }
        }
    }

    public string StatusText => Disabled ? "Đã khóa" : "Hoạt động";
    public string StatusColor => Disabled ? "#DC3545" : "#28A745";
    public string ToggleActionText => Disabled ? "Mở khóa" : "Khóa tài khoản";

    public string GroupsText
    {
        get => _groupsText;
        set
        {
            if (_groupsText != value)
            {
                _groupsText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GroupsText)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public UiUserItem(UserInfo raw)
    {
        Id = raw.Id;
        Username = raw.Username;
        _disabled = raw.Disabled;
        _groupsText = raw.GroupIds.Count > 0 ? string.Join(", ", raw.GroupIds) : "Không có";
    }

    public UiUserItem(string id, string username, bool disabled, string groups)
    {
        Id = id;
        Username = username;
        _disabled = disabled;
        _groupsText = groups;
    }
}

public sealed class UiGroupItem : INotifyPropertyChanged
{
    private string _name = "";
    private string _memberCountText = "";

    public string Id { get; }
    public ObservableCollection<string> Members { get; } = [];

    public string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }
    }

    public string MemberCountText
    {
        get => _memberCountText;
        set
        {
            if (_memberCountText != value)
            {
                _memberCountText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MemberCountText)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public UiGroupItem(GroupInfo raw)
    {
        Id = raw.Id;
        _name = raw.Name;
        foreach (var m in raw.MemberIds) Members.Add(m);
        _memberCountText = $"{Members.Count} thành viên";
    }

    public UiGroupItem(string id, string name, int count)
    {
        Id = id;
        _name = name;
        _memberCountText = $"{count} thành viên";
    }
}

public sealed class UiGrantItem
{
    public string Id { get; }
    public string RoleId { get; }
    public string Subject { get; }
    public string TargetPath { get; }
    public string InheritText { get; }
    public string SourceText { get; }

    public UiGrantItem(GrantInfo raw)
    {
        Id = raw.Id;
        RoleId = raw.RoleId;
        Subject = $"{raw.SubjectType}: {raw.SubjectId}";
        TargetPath = raw.Path ?? "(Hệ thống toàn cục)";
        InheritText = "Kế thừa";
        SourceText = raw.Path != null ? "Trực tiếp" : "Toàn cục";
    }

    public UiGrantItem(string id, string roleId, string subject, string path, bool inherit, string source = "Trực tiếp")
    {
        Id = id;
        RoleId = roleId;
        Subject = subject;
        TargetPath = path;
        InheritText = inherit ? "Có" : "Không";
        SourceText = source;
    }
}
