using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dfs.Shared;

namespace Dfs.Client.Desktop.ViewModels;

public partial class AdminPanelViewModel : ViewModelBase
{
    private readonly IDfsClient _client;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private ObservableCollection<DfsUser> _users = [];

    [ObservableProperty]
    private ObservableCollection<DfsGroup> _groups = [];

    [ObservableProperty]
    private ObservableCollection<DfsGrant> _grants = [];

    // New User form inputs
    [ObservableProperty]
    private string _newUsername = string.Empty;

    [ObservableProperty]
    private string _newFullName = string.Empty;

    [ObservableProperty]
    private string _newInitialPassword = string.Empty;

    [ObservableProperty]
    private string _newUserGroup = "Team A";

    // New Group form inputs
    [ObservableProperty]
    private string _newGroupName = string.Empty;

    // New Grant form inputs
    [ObservableProperty]
    private string _grantPath = "/projects";

    [ObservableProperty]
    private string _grantSubject = "Team A";

    [ObservableProperty]
    private string _grantRole = "Editor";

    [ObservableProperty]
    private string _statusMessage = "Sẵn sàng quản trị.";

    public AdminPanelViewModel(IDfsClient client)
    {
        _client = client;
        _ = LoadAllDataAsync();
    }

    [RelayCommand]
    public async Task LoadAllDataAsync()
    {
        var users = await _client.ListUsersAsync();
        Users = new ObservableCollection<DfsUser>(users);

        var groups = await _client.ListGroupsAsync();
        Groups = new ObservableCollection<DfsGroup>(groups);

        var grants = await _client.ListGrantsAsync("/projects");
        Grants = new ObservableCollection<DfsGrant>(grants);
    }

    [RelayCommand]
    public async Task CreateUserAsync()
    {
        if (string.IsNullOrWhiteSpace(NewUsername))
        {
            StatusMessage = "Tên đăng nhập không được để trống.";
            return;
        }

        await _client.CreateUserAsync(NewUsername.Trim(), NewInitialPassword, NewFullName, NewUserGroup);
        NewUsername = string.Empty;
        NewFullName = string.Empty;
        NewInitialPassword = string.Empty;

        await LoadAllDataAsync();
        StatusMessage = "Tạo tài khoản người dùng mới thành công.";
    }

    [RelayCommand]
    public async Task ToggleDisableUserAsync(string? username)
    {
        if (string.IsNullOrWhiteSpace(username)) return;
        await _client.ToggleDisableUserAsync(username);
        await LoadAllDataAsync();
        StatusMessage = $"Đã cập nhật trạng thái tài khoản {username}.";
    }

    [RelayCommand]
    public async Task CreateGroupAsync()
    {
        if (string.IsNullOrWhiteSpace(NewGroupName)) return;
        await _client.CreateGroupAsync(NewGroupName.Trim());
        NewGroupName = string.Empty;
        await LoadAllDataAsync();
        StatusMessage = "Tạo nhóm mới thành công.";
    }

    [RelayCommand]
    public async Task AddGrantAsync()
    {
        if (string.IsNullOrWhiteSpace(GrantPath) || string.IsNullOrWhiteSpace(GrantSubject)) return;
        await _client.SetGrantAsync(GrantPath, "Group", GrantSubject, GrantRole);
        await LoadAllDataAsync();
        StatusMessage = $"Đã cấp vai trò {GrantRole} cho {GrantSubject} trên {GrantPath}.";
    }

    [RelayCommand]
    public async Task RevokeGrantAsync(string? grantId)
    {
        if (string.IsNullOrWhiteSpace(grantId)) return;
        await _client.RevokeGrantAsync(grantId);
        await LoadAllDataAsync();
        StatusMessage = "Đã thu hồi quyền.";
    }
}
