using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dfs.Fakes;
using Dfs.Shared;

namespace Dfs.Client.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IDfsClient _client;

    [ObservableProperty]
    private ViewModelBase _currentPage;

    [ObservableProperty]
    private bool _isLoggedIn;

    [ObservableProperty]
    private string _currentUsername = "Chưa đăng nhập";

    [ObservableProperty]
    private string _currentUserRoles = string.Empty;

    [ObservableProperty]
    private bool _isAdmin;

    [ObservableProperty]
    private string _connectedEndpoint = "192.168.10.11:7000";

    [ObservableProperty]
    private string _statusText = "Sẵn sàng kết nối | Giao thức TCP v1";

    // Cached sub-viewmodels
    private FileBrowserViewModel? _fileBrowserVm;
    private TransferManagerViewModel? _transfersVm;
    private AdminPanelViewModel? _adminPanelVm;
    private ClusterStatusViewModel? _clusterStatusVm;

    public MainViewModel() : this(new FakeDfsClient())
    {
    }

    public MainViewModel(IDfsClient client)
    {
        _client = client;

        // If client already has a session (like FakeDfsClient defaults to an.nguyen)
        if (_client.CurrentUser != null)
        {
            ApplyLoggedInUser(_client.CurrentUser);
            _fileBrowserVm = new FileBrowserViewModel(_client, NavigateByName);
            _currentPage = _fileBrowserVm;
        }
        else
        {
            _currentPage = new LoginViewModel(_client, OnLoginSuccess);
        }
    }

    private void ApplyLoggedInUser(UserInfo user)
    {
        IsLoggedIn = true;
        CurrentUsername = user.Username;
        CurrentUserRoles = $"{string.Join(", ", user.Roles)} | Team A";
        IsAdmin = user.IsAdmin;
        ConnectedEndpoint = _client.ServerEndpoint;
        StatusText = $"Đã kết nối Metadata Server ({ConnectedEndpoint}) | Phiên làm việc hợp lệ";
    }

    public void OnLoginSuccess(UserInfo user)
    {
        ApplyLoggedInUser(user);
        NavigateToFileBrowser();
    }

    [RelayCommand]
    public void NavigateToFileBrowser()
    {
        _fileBrowserVm ??= new FileBrowserViewModel(_client, NavigateByName);
        CurrentPage = _fileBrowserVm;
    }

    [RelayCommand]
    public void NavigateToTransfers()
    {
        _transfersVm ??= new TransferManagerViewModel(_client);
        _ = _transfersVm.LoadTransfersAsync();
        CurrentPage = _transfersVm;
    }

    [RelayCommand]
    public void NavigateToAdmin()
    {
        if (!IsAdmin) return;
        _adminPanelVm ??= new AdminPanelViewModel(_client);
        _ = _adminPanelVm.LoadAllDataAsync();
        CurrentPage = _adminPanelVm;
    }

    [RelayCommand]
    public void NavigateToCluster()
    {
        _clusterStatusVm ??= new ClusterStatusViewModel(_client);
        _ = _clusterStatusVm.LoadClusterStatusAsync();
        CurrentPage = _clusterStatusVm;
    }

    private void NavigateByName(string viewName)
    {
        if (viewName.Equals("Transfers", StringComparison.OrdinalIgnoreCase))
        {
            NavigateToTransfers();
        }
        else
        {
            NavigateToFileBrowser();
        }
    }

    [RelayCommand]
    public async Task LogoutAsync()
    {
        await _client.LogoutAsync();
        IsLoggedIn = false;
        CurrentUsername = "Chưa đăng nhập";
        CurrentUserRoles = string.Empty;
        IsAdmin = false;
        StatusText = "Đã đăng xuất khỏi hệ thống.";
        CurrentPage = new LoginViewModel(_client, OnLoginSuccess);
    }
}
