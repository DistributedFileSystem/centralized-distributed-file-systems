using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Dfs.Client.Desktop.Models;
using Dfs.Shared.Client;
using Dfs.Shared.Messages;

namespace Dfs.Client.Desktop.ViewModels;

public sealed class FileBrowserViewModel : ViewModelBase
{
    private readonly IDfsClient _client;
    private string _currentPath = "/projects";
    private UiFileItem? _selectedItem;
    private string _statusText = "";
    private bool _isLoading;

    // Notification banner
    private string _notificationMessage = "";
    private bool _notificationIsError;

    // Dialog states
    private bool _isCreateFolderDialogOpen;
    private string _newFolderName = "";

    private bool _isRenameDialogOpen;
    private string _renameNewName = "";

    private bool _isDeleteDialogOpen;
    private string _deletePromptMessage = "";

    private bool _isMoveDialogOpen;
    private string _moveTargetPath = "";

    public ObservableCollection<UiFileItem> Items { get; } = [];
    public ObservableCollection<UiDirectoryNode> RootNodes { get; } = [];

    public string CurrentPath
    {
        get => _currentPath;
        set => SetProperty(ref _currentPath, value);
    }

    public UiFileItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            SetProperty(ref _selectedItem, value);
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(CanDeleteOrRename));
        }
    }

    public bool HasSelection => _selectedItem != null;
    public bool CanDeleteOrRename => _selectedItem != null && !IsLoading;

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            SetProperty(ref _isLoading, value);
            OnPropertyChanged(nameof(CanDeleteOrRename));
        }
    }

    // Notification Properties
    public string NotificationMessage
    {
        get => _notificationMessage;
        set
        {
            SetProperty(ref _notificationMessage, value);
            OnPropertyChanged(nameof(HasNotification));
        }
    }

    public bool NotificationIsError
    {
        get => _notificationIsError;
        set => SetProperty(ref _notificationIsError, value);
    }

    public bool HasNotification => !string.IsNullOrEmpty(_notificationMessage);

    // Create Folder Dialog Properties
    public bool IsCreateFolderDialogOpen
    {
        get => _isCreateFolderDialogOpen;
        set => SetProperty(ref _isCreateFolderDialogOpen, value);
    }

    public string NewFolderName
    {
        get => _newFolderName;
        set => SetProperty(ref _newFolderName, value);
    }

    // Rename Dialog Properties
    public bool IsRenameDialogOpen
    {
        get => _isRenameDialogOpen;
        set => SetProperty(ref _isRenameDialogOpen, value);
    }

    public string RenameNewName
    {
        get => _renameNewName;
        set => SetProperty(ref _renameNewName, value);
    }

    // Delete Dialog Properties
    public bool IsDeleteDialogOpen
    {
        get => _isDeleteDialogOpen;
        set => SetProperty(ref _isDeleteDialogOpen, value);
    }

    public string DeletePromptMessage
    {
        get => _deletePromptMessage;
        set => SetProperty(ref _deletePromptMessage, value);
    }

    // Move Dialog Properties
    public bool IsMoveDialogOpen
    {
        get => _isMoveDialogOpen;
        set => SetProperty(ref _isMoveDialogOpen, value);
    }

    public string MoveTargetPath
    {
        get => _moveTargetPath;
        set => SetProperty(ref _moveTargetPath, value);
    }

    // Commands
    public ICommand RefreshCommand { get; }
    public ICommand NavigateUpCommand { get; }
    public ICommand OpenItemCommand { get; }
    public ICommand SelectTreeNodeCommand { get; }
    public ICommand DismissNotificationCommand { get; }

    // Dialog Commands
    public ICommand OpenCreateFolderDialogCommand { get; }
    public ICommand ConfirmCreateFolderCommand { get; }
    public ICommand CancelCreateFolderCommand { get; }

    public ICommand OpenRenameDialogCommand { get; }
    public ICommand ConfirmRenameCommand { get; }
    public ICommand CancelRenameCommand { get; }

    public ICommand OpenDeleteDialogCommand { get; }
    public ICommand ConfirmDeleteCommand { get; }
    public ICommand CancelDeleteCommand { get; }

    public ICommand OpenMoveDialogCommand { get; }
    public ICommand ConfirmMoveCommand { get; }
    public ICommand CancelMoveCommand { get; }

    public FileBrowserViewModel(IDfsClient client)
    {
        _client = client;

        // Initialize TreeView Root
        var root = new UiDirectoryNode("/", "/") { IsExpanded = true };
        var projects = new UiDirectoryNode("projects", "/projects") { IsExpanded = true };
        projects.Children.Add(new UiDirectoryNode("dfs", "/projects/dfs"));
        root.Children.Add(projects);
        RootNodes.Add(root);

        RefreshCommand = new RelayCommand(async () => await LoadDirectoryAsync(CurrentPath));
        NavigateUpCommand = new RelayCommand(async () => await NavigateUpAsync());
        DismissNotificationCommand = new RelayCommand(() => NotificationMessage = "");

        OpenItemCommand = new RelayCommand(async param =>
        {
            if (param is UiFileItem item && item.IsFolder)
            {
                await LoadDirectoryAsync(item.Path);
            }
        });

        SelectTreeNodeCommand = new RelayCommand(async param =>
        {
            if (param is UiDirectoryNode node)
            {
                await LoadDirectoryAsync(node.FullPath);
            }
        });

        // Create Folder
        OpenCreateFolderDialogCommand = new RelayCommand(() =>
        {
            NewFolderName = "";
            IsCreateFolderDialogOpen = true;
        });
        ConfirmCreateFolderCommand = new RelayCommand(async () => await ExecuteCreateFolderAsync());
        CancelCreateFolderCommand = new RelayCommand(() => IsCreateFolderDialogOpen = false);

        // Rename
        OpenRenameDialogCommand = new RelayCommand(() =>
        {
            if (SelectedItem == null) return;
            RenameNewName = SelectedItem.Name;
            IsRenameDialogOpen = true;
        });
        ConfirmRenameCommand = new RelayCommand(async () => await ExecuteRenameAsync());
        CancelRenameCommand = new RelayCommand(() => IsRenameDialogOpen = false);

        // Delete
        OpenDeleteDialogCommand = new RelayCommand(() =>
        {
            if (SelectedItem == null) return;
            DeletePromptMessage = $"Bạn có chắc chắn muốn xóa mục '{SelectedItem.Name}' ({(SelectedItem.IsFolder ? "Thư mục" : "Tệp tin")}) vĩnh viễn không?";
            IsDeleteDialogOpen = true;
        });
        ConfirmDeleteCommand = new RelayCommand(async () => await ExecuteDeleteAsync());
        CancelDeleteCommand = new RelayCommand(() => IsDeleteDialogOpen = false);

        // Move
        OpenMoveDialogCommand = new RelayCommand(() =>
        {
            if (SelectedItem == null) return;
            MoveTargetPath = "/projects";
            IsMoveDialogOpen = true;
        });
        ConfirmMoveCommand = new RelayCommand(async () => await ExecuteMoveAsync());
        CancelMoveCommand = new RelayCommand(() => IsMoveDialogOpen = false);
    }

    public async Task LoadDirectoryAsync(string path)
    {
        if (IsLoading) return;

        IsLoading = true;
        StatusText = "Đang tải danh sách tệp...";
        NotificationMessage = "";

        try
        {
            var results = await _client.ListDirAsync(path);
            Items.Clear();
            foreach (var item in results.OrderByDescending(i => i.Kind == ItemKind.Folder).ThenBy(i => i.Name))
            {
                Items.Add(new UiFileItem(item));
            }

            CurrentPath = path;
            UpdateTreeNodes(path, results);

            var fileCount = Items.Count(i => !i.IsFolder);
            var folderCount = Items.Count(i => i.IsFolder);
            StatusText = $"{Items.Count} mục ({folderCount} thư mục, {fileCount} tệp)";
        }
        catch (DfsException ex)
        {
            ShowNotification(ex.Code switch
            {
                ErrorCode.NotFound => $"Không tìm thấy thư mục '{path}'.",
                ErrorCode.Forbidden => $"Bạn không có quyền truy cập vào '{path}'.",
                ErrorCode.Unauthorized => "Phiên làm việc đã hết hạn. Vui lòng đăng nhập lại.",
                _ => $"Lỗi duyệt thư mục: {ex.Message} ({ex.Code})"
            }, isError: true);
            StatusText = $"Lỗi: {ex.Message}";
        }
        catch (Exception ex)
        {
            ShowNotification($"Lỗi tải thư mục: {ex.Message}", isError: true);
            StatusText = $"Lỗi: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task NavigateUpAsync()
    {
        if (string.IsNullOrEmpty(CurrentPath) || CurrentPath == "/")
        {
            await LoadDirectoryAsync("/");
            return;
        }

        var trimmed = CurrentPath.TrimEnd('/');
        var lastSlash = trimmed.LastIndexOf('/');
        var parent = lastSlash <= 0 ? "/" : trimmed[..lastSlash];
        await LoadDirectoryAsync(parent);
    }

    private async Task ExecuteCreateFolderAsync()
    {
        var folderName = NewFolderName?.Trim();
        if (string.IsNullOrWhiteSpace(folderName))
        {
            ShowNotification("Tên thư mục không được để trống.", isError: true);
            return;
        }

        if (Items.Any(i => i.Name.Equals(folderName, StringComparison.OrdinalIgnoreCase)))
        {
            ShowNotification($"Thư mục '{folderName}' đã tồn tại trong thư mục hiện tại.", isError: true);
            return;
        }

        IsCreateFolderDialogOpen = false;
        var folderPath = CurrentPath == "/" ? $"/{folderName}" : $"{CurrentPath}/{folderName}";

        try
        {
            ItemInfo created;
            try
            {
                created = await _client.CreateFolderAsync(folderPath);
            }
            catch (NotSupportedException)
            {
                // In-memory demo fallback for FakeDfsClient
                created = new ItemInfo(
                    Id: $"i-{Guid.NewGuid():N}"[..8],
                    Path: folderPath,
                    Name: folderName,
                    Kind: ItemKind.Folder,
                    Size: 0,
                    OwnerId: "demo",
                    CurrentVersion: 0,
                    ModifiedAt: DateTime.UtcNow);
            }

            Items.Insert(0, new UiFileItem(created));
            AddFolderToTree(folderPath, folderName);
            ShowNotification($"Đã tạo thư mục '{folderName}' thành công.", isError: false);
            StatusText = $"Đã tạo thư mục: {folderPath}";
        }
        catch (DfsException ex)
        {
            ShowNotification(ex.Code switch
            {
                ErrorCode.AlreadyExists => $"Thư mục '{folderName}' đã tồn tại.",
                ErrorCode.Forbidden => "Bạn không có quyền tạo thư mục tại đây.",
                _ => $"Lỗi tạo thư mục: {ex.Message} ({ex.Code})"
            }, isError: true);
        }
        catch (Exception ex)
        {
            ShowNotification($"Lỗi tạo thư mục: {ex.Message}", isError: true);
        }
    }

    private async Task ExecuteRenameAsync()
    {
        if (SelectedItem == null) return;

        var targetItem = SelectedItem;
        var newName = RenameNewName?.Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            ShowNotification("Tên mới không được để trống.", isError: true);
            return;
        }

        if (newName == targetItem.Name)
        {
            IsRenameDialogOpen = false;
            return;
        }

        if (Items.Any(i => i != targetItem && i.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
        {
            ShowNotification($"Mục '{newName}' đã tồn tại trong thư mục hiện tại.", isError: true);
            return;
        }

        IsRenameDialogOpen = false;
        var oldPath = targetItem.Path;
        var parentDir = Path.GetDirectoryName(oldPath)?.Replace('\\', '/') ?? "/";
        var newPath = parentDir == "/" ? $"/{newName}" : $"{parentDir}/{newName}";

        try
        {
            ItemInfo renamed;
            try
            {
                renamed = await _client.RenameAsync(oldPath, newName);
            }
            catch (NotSupportedException)
            {
                // In-memory demo fallback for FakeDfsClient
                renamed = new ItemInfo(
                    Id: targetItem.Raw.Id,
                    Path: newPath,
                    Name: newName,
                    Kind: targetItem.Raw.Kind,
                    Size: targetItem.Raw.Size,
                    OwnerId: targetItem.Raw.OwnerId,
                    CurrentVersion: targetItem.Raw.CurrentVersion,
                    ModifiedAt: DateTime.UtcNow);
            }

            var index = Items.IndexOf(targetItem);
            if (index >= 0)
            {
                Items[index] = new UiFileItem(renamed);
                SelectedItem = Items[index];
            }

            ShowNotification($"Đã đổi tên '{targetItem.Name}' thành '{newName}'.", isError: false);
            StatusText = $"Đã đổi tên: {newPath}";
        }
        catch (DfsException ex)
        {
            ShowNotification(ex.Code switch
            {
                ErrorCode.NotFound => "Không tìm thấy mục cần đổi tên.",
                ErrorCode.AlreadyExists => $"Tên '{newName}' đã tồn tại trong thư mục.",
                ErrorCode.Forbidden => "Bạn không có quyền đổi tên mục này.",
                _ => $"Lỗi đổi tên: {ex.Message} ({ex.Code})"
            }, isError: true);
        }
        catch (Exception ex)
        {
            ShowNotification($"Lỗi đổi tên: {ex.Message}", isError: true);
        }
    }

    private async Task ExecuteDeleteAsync()
    {
        if (SelectedItem == null) return;

        var targetItem = SelectedItem;
        IsDeleteDialogOpen = false;

        try
        {
            try
            {
                await _client.DeleteItemAsync(targetItem.Path);
            }
            catch (NotSupportedException)
            {
                // In-memory demo fallback for FakeDfsClient
            }

            Items.Remove(targetItem);
            SelectedItem = null;
            ShowNotification($"Đã xóa '{targetItem.Name}' thành công.", isError: false);
            StatusText = $"Đã xóa mục: {targetItem.Path}";
        }
        catch (DfsException ex)
        {
            ShowNotification(ex.Code switch
            {
                ErrorCode.NotFound => "Mục đã bị xóa hoặc không tìm thấy.",
                ErrorCode.Forbidden => "Bạn không có quyền xóa mục này.",
                _ => $"Lỗi xóa mục: {ex.Message} ({ex.Code})"
            }, isError: true);
        }
        catch (Exception ex)
        {
            ShowNotification($"Lỗi xóa: {ex.Message}", isError: true);
        }
    }

    private async Task ExecuteMoveAsync()
    {
        if (SelectedItem == null) return;

        var targetItem = SelectedItem;
        var destinationFolder = MoveTargetPath?.Trim();
        if (string.IsNullOrWhiteSpace(destinationFolder))
        {
            ShowNotification("Đường dẫn đích không được để trống.", isError: true);
            return;
        }

        IsMoveDialogOpen = false;

        try
        {
            ItemInfo moved;
            try
            {
                moved = await _client.MoveAsync(targetItem.Path, destinationFolder);
            }
            catch (NotSupportedException)
            {
                var newPath = destinationFolder == "/" ? $"/{targetItem.Name}" : $"{destinationFolder}/{targetItem.Name}";
                moved = new ItemInfo(
                    Id: targetItem.Raw.Id,
                    Path: newPath,
                    Name: targetItem.Name,
                    Kind: targetItem.Raw.Kind,
                    Size: targetItem.Raw.Size,
                    OwnerId: targetItem.Raw.OwnerId,
                    CurrentVersion: targetItem.Raw.CurrentVersion,
                    ModifiedAt: DateTime.UtcNow);
            }

            // Remove from current view if moved outside
            if (destinationFolder != CurrentPath)
            {
                Items.Remove(targetItem);
                SelectedItem = null;
            }

            ShowNotification($"Đã di chuyển '{targetItem.Name}' sang '{destinationFolder}'.", isError: false);
            StatusText = $"Đã di chuyển tới: {destinationFolder}";
        }
        catch (DfsException ex)
        {
            ShowNotification(ex.Code switch
            {
                ErrorCode.NotFound => "Đường dẫn nguồn hoặc đích không tồn tại.",
                ErrorCode.AlreadyExists => "Tệp hoặc thư mục trùng tên đã tồn tại ở đích đến.",
                ErrorCode.Forbidden => "Bạn không có quyền di chuyển mục này.",
                _ => $"Lỗi di chuyển: {ex.Message} ({ex.Code})"
            }, isError: true);
        }
        catch (Exception ex)
        {
            ShowNotification($"Lỗi di chuyển: {ex.Message}", isError: true);
        }
    }

    private void ShowNotification(string message, bool isError)
    {
        NotificationMessage = message;
        NotificationIsError = isError;
    }

    private void UpdateTreeNodes(string path, IReadOnlyList<ItemInfo> children)
    {
        var folderChildren = children.Where(c => c.Kind == ItemKind.Folder).ToList();
        if (folderChildren.Count == 0) return;

        var targetNode = FindNode(RootNodes, path);
        if (targetNode == null) return;

        foreach (var folder in folderChildren)
        {
            if (!targetNode.Children.Any(c => c.FullPath == folder.Path))
            {
                targetNode.Children.Add(new UiDirectoryNode(folder.Name, folder.Path));
            }
        }
    }

    private void AddFolderToTree(string fullPath, string name)
    {
        var parentPath = Path.GetDirectoryName(fullPath)?.Replace('\\', '/') ?? "/";
        var parentNode = FindNode(RootNodes, parentPath);
        if (parentNode != null && !parentNode.Children.Any(c => c.FullPath == fullPath))
        {
            parentNode.Children.Add(new UiDirectoryNode(name, fullPath));
            parentNode.IsExpanded = true;
        }
    }

    private static UiDirectoryNode? FindNode(IEnumerable<UiDirectoryNode> nodes, string path)
    {
        foreach (var node in nodes)
        {
            if (node.FullPath == path) return node;
            var found = FindNode(node.Children, path);
            if (found != null) return found;
        }
        return null;
    }
}
