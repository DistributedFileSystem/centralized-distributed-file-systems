using System;
using System.Windows.Input;
using Dfs.Shared.Client;
using Dfs.Shared.Messages;

namespace Dfs.Client.Desktop.ViewModels;

public sealed class AdminShellViewModel : ViewModelBase
{
    private readonly IDfsClient _client;
    private readonly Action _onLogout;

    public UserRef CurrentUser { get; }

    public AdminPanelViewModel AdminPanelVm { get; }

    public ICommand NavigateUsersCommand { get; }
    public ICommand NavigateGroupsCommand { get; }
    public ICommand NavigateGrantsCommand { get; }
    public ICommand LogoutCommand { get; }

    public AdminShellViewModel(IDfsClient client, UserRef user, Action onLogout)
    {
        _client = client;
        CurrentUser = user;
        _onLogout = onLogout;

        AdminPanelVm = new AdminPanelViewModel(_client);

        NavigateUsersCommand = new RelayCommand(async () =>
        {
            AdminPanelVm.SelectedTabIndex = 0;
            await AdminPanelVm.LoadDataAsync();
        });

        NavigateGroupsCommand = new RelayCommand(async () =>
        {
            AdminPanelVm.SelectedTabIndex = 1;
            await AdminPanelVm.LoadDataAsync();
        });

        NavigateGrantsCommand = new RelayCommand(async () =>
        {
            AdminPanelVm.SelectedTabIndex = 2;
            await AdminPanelVm.LoadDataAsync();
        });

        LogoutCommand = new RelayCommand(() => _onLogout());

        _ = AdminPanelVm.LoadDataAsync();
    }
}
