using System;
using System.Collections.ObjectModel;
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

    public ObservableCollection<UiFileItem> Items { get; } = [];

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
        }
    }

    public bool HasSelection => _selectedItem != null;

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand NavigateUpCommand { get; }
    public ICommand OpenItemCommand { get; }

    public FileBrowserViewModel(IDfsClient client)
    {
        _client = client;
        RefreshCommand = new RelayCommand(async () => await LoadDirectoryAsync(CurrentPath));
        NavigateUpCommand = new RelayCommand(async () => await NavigateUpAsync());
        OpenItemCommand = new RelayCommand(async param =>
        {
            if (param is UiFileItem item && item.IsFolder)
            {
                await LoadDirectoryAsync(item.Path);
            }
        });
    }

    public async Task LoadDirectoryAsync(string path)
    {
        if (IsLoading) return;

        IsLoading = true;
        StatusText = "Đang tải danh sách tệp...";

        try
        {
            var results = await _client.ListDirAsync(path);
            Items.Clear();
            foreach (var item in results.OrderByDescending(i => i.Kind == ItemKind.Folder).ThenBy(i => i.Name))
            {
                Items.Add(new UiFileItem(item));
            }

            CurrentPath = path;
            var fileCount = Items.Count(i => !i.IsFolder);
            var folderCount = Items.Count(i => i.IsFolder);
            StatusText = $"{Items.Count} mục ({folderCount} thư mục, {fileCount} tệp)";
        }
        catch (DfsException ex)
        {
            StatusText = $"Lỗi: {ex.Message} ({ex.Code})";
        }
        catch (Exception ex)
        {
            StatusText = $"Lỗi: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task NavigateUpAsync()
    {
        if (string.IsNullOrEmpty(CurrentPath) || CurrentPath == "/" || CurrentPath == "/projects")
        {
            await LoadDirectoryAsync("/");
            return;
        }

        var lastSlash = CurrentPath.LastIndexOf('/');
        var parent = lastSlash <= 0 ? "/" : CurrentPath[..lastSlash];
        await LoadDirectoryAsync(parent);
    }
}
