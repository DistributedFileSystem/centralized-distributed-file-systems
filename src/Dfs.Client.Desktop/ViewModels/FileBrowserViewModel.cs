using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dfs.Shared;

namespace Dfs.Client.Desktop.ViewModels;

public class FolderNode
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Icon { get; set; } = "📁";
    public ObservableCollection<FolderNode> SubFolders { get; set; } = [];
}

public partial class FileBrowserViewModel : ViewModelBase
{
    private readonly IDfsClient _client;
    private readonly Action<string> _onNavigateToTransfers;

    [ObservableProperty]
    private string _currentPath = "/projects/dfs";

    [ObservableProperty]
    private ObservableCollection<DfsItem> _items = [];

    [ObservableProperty]
    private DfsItem? _selectedItem;

    [ObservableProperty]
    private ObservableCollection<FolderNode> _folderTree = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "Sẵn sàng";

    [ObservableProperty]
    private bool _showVersionHistoryModal;

    [ObservableProperty]
    private ObservableCollection<FileVersionInfo> _currentVersions = [];

    [ObservableProperty]
    private string _versionHistoryFileName = string.Empty;

    public FileBrowserViewModel(IDfsClient client, Action<string> onNavigateToTransfers)
    {
        _client = client;
        _onNavigateToTransfers = onNavigateToTransfers;

        BuildFolderTree();
        _ = LoadDirectoryAsync(CurrentPath);
    }

    private void BuildFolderTree()
    {
        var root = new FolderNode
        {
            Name = "Root (/)",
            Path = "/",
            SubFolders = [
                new FolderNode
                {
                    Name = "projects",
                    Path = "/projects",
                    SubFolders = [
                        new FolderNode { Name = "dfs", Path = "/projects/dfs" },
                        new FolderNode { Name = "ai_lab", Path = "/projects/ai_lab" }
                    ]
                },
                new FolderNode { Name = "shared", Path = "/shared" },
                new FolderNode
                {
                    Name = "users",
                    Path = "/users",
                    SubFolders = [
                        new FolderNode { Name = "an.nguyen", Path = "/users/an.nguyen" }
                    ]
                }
            ]
        };
        FolderTree = [root];
    }

    [RelayCommand]
    public async Task LoadDirectoryAsync(string path)
    {
        IsLoading = true;
        CurrentPath = string.IsNullOrWhiteSpace(path) ? "/" : path;
        StatusMessage = $"Đang tải danh mục {CurrentPath}...";

        try
        {
            var results = await _client.ListDirAsync(CurrentPath);
            Items.Clear();
            foreach (var item in results)
            {
                Items.Add(item);
            }
            StatusMessage = $"{Items.Count} mục trong thư mục | Metadata Server: Healthy";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi tải thư mục: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task NavigateUpAsync()
    {
        if (CurrentPath == "/" || string.IsNullOrWhiteSpace(CurrentPath)) return;

        var lastSlash = CurrentPath.TrimEnd('/').LastIndexOf('/');
        var parentPath = lastSlash <= 0 ? "/" : CurrentPath[..lastSlash];
        await LoadDirectoryAsync(parentPath);
    }

    [RelayCommand]
    public async Task ItemDoubleClickedAsync(DfsItem? item)
    {
        if (item == null) return;

        if (item.IsDirectory)
        {
            var nextPath = CurrentPath == "/" ? $"/{item.Name}" : $"{CurrentPath}/{item.Name}";
            await LoadDirectoryAsync(nextPath);
        }
        else
        {
            await DownloadAsync();
        }
    }

    [RelayCommand]
    public async Task CreateFolderAsync()
    {
        var folderName = $"new_folder_{DateTime.Now:HHmmss}";
        var path = CurrentPath == "/" ? $"/{folderName}" : $"{CurrentPath}/{folderName}";
        await _client.CreateFolderAsync(path);
        await LoadDirectoryAsync(CurrentPath);
        StatusMessage = $"Đã tạo thư mục {folderName} thành công.";
    }

    [RelayCommand]
    public async Task DeleteAsync()
    {
        if (SelectedItem == null) return;
        var itemName = SelectedItem.Name;
        var path = CurrentPath == "/" ? $"/{itemName}" : $"{CurrentPath}/{itemName}";
        await _client.DeleteItemAsync(path);
        await LoadDirectoryAsync(CurrentPath);
        StatusMessage = $"Đã xóa mục '{itemName}' thành công.";
    }

    [RelayCommand]
    public async Task RenameAsync()
    {
        if (SelectedItem == null) return;
        var oldName = SelectedItem.Name;
        var ext = Path.GetExtension(oldName);
        var baseName = Path.GetFileNameWithoutExtension(oldName);
        var newName = $"{baseName}_renamed{ext}";

        var oldPath = CurrentPath == "/" ? $"/{oldName}" : $"{CurrentPath}/{oldName}";
        var newPath = CurrentPath == "/" ? $"/{newName}" : $"{CurrentPath}/{newName}";

        await _client.RenameAsync(oldPath, newPath);
        await LoadDirectoryAsync(CurrentPath);
        StatusMessage = $"Đã đổi tên '{oldName}' -> '{newName}'.";
    }

    [RelayCommand]
    public async Task UploadAsync()
    {
        var sampleFile = "uploaded_doc.pdf";
        var remotePath = CurrentPath == "/" ? $"/{sampleFile}" : $"{CurrentPath}/{sampleFile}";
        StatusMessage = $"Đang tải lên {sampleFile} vào 5 Data Server...";
        await _client.UploadFileAsync(sampleFile, remotePath);
        await LoadDirectoryAsync(CurrentPath);
        _onNavigateToTransfers("Transfers");
    }

    [RelayCommand]
    public async Task DownloadAsync()
    {
        if (SelectedItem == null || SelectedItem.IsDirectory) return;
        var remotePath = CurrentPath == "/" ? $"/{SelectedItem.Name}" : $"{CurrentPath}/{SelectedItem.Name}";
        StatusMessage = $"Bắt đầu tải xuống {SelectedItem.Name}...";
        await _client.DownloadFileAsync(remotePath, $"C:\\Downloads\\{SelectedItem.Name}");
        _onNavigateToTransfers("Transfers");
    }

    [RelayCommand]
    public async Task ShowVersionHistoryAsync()
    {
        if (SelectedItem == null || SelectedItem.IsDirectory) return;

        VersionHistoryFileName = SelectedItem.Name;
        var path = CurrentPath == "/" ? $"/{SelectedItem.Name}" : $"{CurrentPath}/{SelectedItem.Name}";
        var versions = await _client.ListVersionsAsync(path);

        CurrentVersions.Clear();
        foreach (var v in versions)
        {
            CurrentVersions.Add(v);
        }

        ShowVersionHistoryModal = true;
    }

    [RelayCommand]
    public void CloseVersionHistory()
    {
        ShowVersionHistoryModal = false;
    }
}
