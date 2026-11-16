using System;
using System.Threading.Tasks;
using Dfs.Shared.Client;
using Dfs.Shared.Messages;
using Dfs.Shared.Security;

namespace Dfs.Client.Desktop.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly IDfsClient _client;
    private object _currentPage;
    private string _windowTitle = "Centralized DFS - Đăng nhập";

    public object CurrentPage
    {
        get => _currentPage;
        set => SetProperty(ref _currentPage, value);
    }

    public string WindowTitle
    {
        get => _windowTitle;
        set => SetProperty(ref _windowTitle, value);
    }

    public LoginViewModel LoginVm { get; }

    public MainViewModel(IDfsClient client)
    {
        _client = client;
        LoginVm = new LoginViewModel(_client, OnLoginSuccess);
        _currentPage = LoginVm;
    }

    private void OnLoginSuccess(LoginResponse response, string targetMode)
    {
        var isAdminRole = response.SystemRoles.Contains(BuiltInRoles.Admin)
            || response.Permissions.Contains(Permission.UserManage);

        var openAdmin = targetMode == "admin" || (targetMode == "auto" && isAdminRole);

        if (openAdmin)
        {
            var adminShell = new AdminShellViewModel(_client, response.User, ExecuteLogout);
            CurrentPage = adminShell;
            WindowTitle = $"Centralized DFS - Admin Control Panel [{response.User.Username}]";
        }
        else
        {
            var userShell = new UserShellViewModel(_client, response.User, ExecuteLogout);
            CurrentPage = userShell;
            WindowTitle = $"Centralized DFS - User Client [{response.User.Username}]";
        }
    }

    private async void ExecuteLogout()
    {
        try
        {
            await _client.LogoutAsync();
        }
        catch
        {
            // Ignore logout exceptions in UI
        }

        CurrentPage = LoginVm;
        WindowTitle = "Centralized DFS - Đăng nhập";
    }
}
