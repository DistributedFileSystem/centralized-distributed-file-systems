using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Dfs.Shared.Client;
using Dfs.Shared.Messages;
using Dfs.Shared.Security;

namespace Dfs.Client.Desktop.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly IDfsClient _client;
    private bool _isLoggedIn;
    private UserRef? _currentUser;
    private bool _isAdmin;
    private string _activeTab = "files";
    private object _currentPage;

    public bool IsLoggedIn
    {
        get => _isLoggedIn;
        set
        {
            SetProperty(ref _isLoggedIn, value);
            OnPropertyChanged(nameof(IsNotLoggedIn));
        }
    }

    public bool IsNotLoggedIn => !_isLoggedIn;

    public UserRef? CurrentUser
    {
        get => _currentUser;
        set
        {
            SetProperty(ref _currentUser, value);
            OnPropertyChanged(nameof(UserDisplayName));
        }
    }

    public string UserDisplayName => CurrentUser != null
        ? $"{CurrentUser.Username} ({(IsAdmin ? "Admin" : "User")})"
        : "";

    public bool IsAdmin
    {
        get => _isAdmin;
        set => SetProperty(ref _isAdmin, value);
    }

    public string ActiveTab
    {
        get => _activeTab;
        set
        {
            SetProperty(ref _activeTab, value);
            OnPropertyChanged(nameof(IsFilesTab));
            OnPropertyChanged(nameof(IsTransfersTab));
            OnPropertyChanged(nameof(IsAdminTab));
            OnPropertyChanged(nameof(IsClusterTab));
        }
    }

    public bool IsFilesTab => _activeTab == "files";
    public bool IsTransfersTab => _activeTab == "transfers";
    public bool IsAdminTab => _activeTab == "admin";
    public bool IsClusterTab => _activeTab == "cluster";

    public object CurrentPage
    {
        get => _currentPage;
        set => SetProperty(ref _currentPage, value);
    }

    public LoginViewModel LoginVm { get; }
    public FileBrowserViewModel FileBrowserVm { get; }
    public TransferManagerViewModel TransferManagerVm { get; }
    public AdminPanelViewModel AdminPanelVm { get; }
    public ClusterStatusViewModel ClusterStatusVm { get; }

    public ICommand NavigateFilesCommand { get; }
    public ICommand NavigateTransfersCommand { get; }
    public ICommand NavigateAdminCommand { get; }
    public ICommand NavigateClusterCommand { get; }
    public ICommand LogoutCommand { get; }

    public MainViewModel(IDfsClient client)
    {
        _client = client;

        LoginVm = new LoginViewModel(_client, OnLoginSuccess);
        FileBrowserVm = new FileBrowserViewModel(_client);
        TransferManagerVm = new TransferManagerViewModel(_client);
        AdminPanelVm = new AdminPanelViewModel(_client);
        ClusterStatusVm = new ClusterStatusViewModel(_client);

        _currentPage = LoginVm;

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

        NavigateAdminCommand = new RelayCommand(async () =>
        {
            ActiveTab = "admin";
            CurrentPage = AdminPanelVm;
            await AdminPanelVm.LoadDataAsync();
        });

        NavigateClusterCommand = new RelayCommand(async () =>
        {
            ActiveTab = "cluster";
            CurrentPage = ClusterStatusVm;
            await ClusterStatusVm.LoadClusterStatusAsync();
        });

        LogoutCommand = new RelayCommand(async () => await ExecuteLogoutAsync());
    }

    private void OnLoginSuccess(LoginResponse response)
    {
        CurrentUser = response.User;
        IsAdmin = response.SystemRoles.Contains(BuiltInRoles.Admin) || response.Permissions.Contains(Permission.UserManage);
        IsLoggedIn = true;

        ActiveTab = "files";
        CurrentPage = FileBrowserVm;
        _ = FileBrowserVm.LoadDirectoryAsync("/projects");
    }

    private async Task ExecuteLogoutAsync()
    {
        try
        {
            await _client.LogoutAsync();
        }
        catch
        {
            // Ignore logout errors
        }

        IsLoggedIn = false;
        CurrentUser = null;
        IsAdmin = false;
        CurrentPage = LoginVm;
    }
}
