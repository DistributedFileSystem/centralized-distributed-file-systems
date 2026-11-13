using System;
using System.Windows.Input;
using Dfs.Shared.Client;
using Dfs.Shared.Messages;

namespace Dfs.Client.Desktop.ViewModels;

public sealed class UserShellViewModel : ViewModelBase
{
    private readonly IDfsClient _client;
    private readonly Action _onLogout;
    private string _activeTab = "files";
    private object _currentPage;

    public UserRef CurrentUser { get; }

    public string ActiveTab
    {
        get => _activeTab;
        set
        {
            if (SetProperty(ref _activeTab, value))
            {
                OnPropertyChanged(nameof(IsFilesTab));
                OnPropertyChanged(nameof(IsTransfersTab));
            }
        }
    }

    public bool IsFilesTab => _activeTab == "files";
    public bool IsTransfersTab => _activeTab == "transfers";

    public object CurrentPage
    {
        get => _currentPage;
        set => SetProperty(ref _currentPage, value);
    }

    public FileBrowserViewModel FileBrowserVm { get; }
    public TransferManagerViewModel TransferManagerVm { get; }

    public ICommand NavigateFilesCommand { get; }
    public ICommand NavigateTransfersCommand { get; }
    public ICommand LogoutCommand { get; }

    public UserShellViewModel(IDfsClient client, UserRef user, Action onLogout)
    {
        _client = client;
        CurrentUser = user;
        _onLogout = onLogout;

        FileBrowserVm = new FileBrowserViewModel(_client);
        TransferManagerVm = new TransferManagerViewModel(_client);

        FileBrowserVm.DownloadRequested = item =>
        {
            var size = item.Raw.Size > 0 ? item.Raw.Size : 2 * 1024 * 1024;
            TransferManagerVm.QueueNewDownload(item.Path, item.Name, size);
        };

        _currentPage = FileBrowserVm;

        NavigateFilesCommand = new RelayCommand(async () =>
        {
            ActiveTab = "files";
            CurrentPage = FileBrowserVm;
            await FileBrowserVm.LoadDirectoryAsync(FileBrowserVm.CurrentPath);
        });

        NavigateTransfersCommand = new RelayCommand(() =>
        {
            ActiveTab = "transfers";
            CurrentPage = TransferManagerVm;
        });

        LogoutCommand = new RelayCommand(() => _onLogout());

        _ = FileBrowserVm.LoadDirectoryAsync("/projects");
    }
}
