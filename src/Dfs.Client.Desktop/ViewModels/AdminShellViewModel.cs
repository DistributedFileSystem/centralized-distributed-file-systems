using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Dfs.Shared.Client;
using Dfs.Shared.Messages;

namespace Dfs.Client.Desktop.ViewModels;

public sealed class AdminShellViewModel : ViewModelBase
{
    private readonly IDfsClient _client;
    private readonly Action _onLogout;
    private string _activeTab = "admin";
    private object _currentPage;

    public UserRef CurrentUser { get; }

    public string ActiveTab
    {
        get => _activeTab;
        set
        {
            if (SetProperty(ref _activeTab, value))
            {
                OnPropertyChanged(nameof(IsAdminTab));
                OnPropertyChanged(nameof(IsClusterTab));
            }
        }
    }

    public bool IsAdminTab => _activeTab == "admin";
    public bool IsClusterTab => _activeTab == "cluster";

    public object CurrentPage
    {
        get => _currentPage;
        set => SetProperty(ref _currentPage, value);
    }

    public AdminPanelViewModel AdminPanelVm { get; }
    public ClusterStatusViewModel ClusterStatusVm { get; }

    public ICommand NavigateAdminCommand { get; }
    public ICommand NavigateClusterCommand { get; }
    public ICommand LogoutCommand { get; }

    public AdminShellViewModel(IDfsClient client, UserRef user, Action onLogout)
    {
        _client = client;
        CurrentUser = user;
        _onLogout = onLogout;

        AdminPanelVm = new AdminPanelViewModel(_client);
        ClusterStatusVm = new ClusterStatusViewModel(_client);

        _currentPage = AdminPanelVm;

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

        LogoutCommand = new RelayCommand(() => _onLogout());

        _ = AdminPanelVm.LoadDataAsync();
    }
}
