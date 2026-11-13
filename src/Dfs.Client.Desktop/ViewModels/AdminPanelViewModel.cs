using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Dfs.Client.Desktop.Models;
using Dfs.Shared.Client;
using Dfs.Shared.Messages;
using Dfs.Shared.Security;

namespace Dfs.Client.Desktop.ViewModels;

public sealed class AdminPanelViewModel : ViewModelBase
{
    private readonly IDfsClient _client;
    private int _selectedTabIndex;
    private string _statusMessage = "";
    private bool _statusIsError;

    // Create User Dialog
    private bool _isCreateUserDialogOpen;
    private string _newUsername = "";
    private string _newUserPassword = "";
    private string _newUserGroup = "Team A";

    // Reset Password Dialog
    private bool _isResetPasswordDialogOpen;
    private UiUserItem? _selectedUserForReset;
    private string _newResetPassword = "";

    // Create Group Dialog
    private bool _isCreateGroupDialogOpen;
    private string _newGroupName = "";

    // Add Member to Group Dialog
    private bool _isAddMemberDialogOpen;
    private UiGroupItem? _selectedGroup;
    private string _addMemberUsername = "";

    // Create Grant Dialog
    private bool _isCreateGrantDialogOpen;
    private string _grantSubjectType = "User"; // "User" or "Group"
    private string _grantSubjectId = "tan.phan";
    private string _grantRoleId = BuiltInRoles.Editor;
    private string _grantPath = "/projects";

    public ObservableCollection<UiUserItem> Users { get; } = [];
    public ObservableCollection<UiGroupItem> Groups { get; } = [];
    public ObservableCollection<UiGrantItem> Grants { get; } = [];

    private string _searchUserQuery = "";
    private string _searchGroupQuery = "";

    public string SearchUserQuery
    {
        get => _searchUserQuery;
        set
        {
            if (SetProperty(ref _searchUserQuery, value))
            {
                OnPropertyChanged(nameof(FilteredUsers));
            }
        }
    }

    public string SearchGroupQuery
    {
        get => _searchGroupQuery;
        set
        {
            if (SetProperty(ref _searchGroupQuery, value))
            {
                OnPropertyChanged(nameof(FilteredGroups));
            }
        }
    }

    public IEnumerable<UiUserItem> FilteredUsers => string.IsNullOrWhiteSpace(_searchUserQuery)
        ? Users
        : Users.Where(u => u.Username.Contains(_searchUserQuery.Trim(), StringComparison.OrdinalIgnoreCase));

    public IEnumerable<UiGroupItem> FilteredGroups => string.IsNullOrWhiteSpace(_searchGroupQuery)
        ? Groups
        : Groups.Where(g => g.Name.Contains(_searchGroupQuery.Trim(), StringComparison.OrdinalIgnoreCase));

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }
    }

    public bool StatusIsError
    {
        get => _statusIsError;
        set => SetProperty(ref _statusIsError, value);
    }

    public bool HasStatusMessage => !string.IsNullOrEmpty(_statusMessage);

    // Create User Dialog Props
    public bool IsCreateUserDialogOpen
    {
        get => _isCreateUserDialogOpen;
        set => SetProperty(ref _isCreateUserDialogOpen, value);
    }

    public string NewUsername
    {
        get => _newUsername;
        set => SetProperty(ref _newUsername, value);
    }

    public string NewUserPassword
    {
        get => _newUserPassword;
        set => SetProperty(ref _newUserPassword, value);
    }

    public string NewUserGroup
    {
        get => _newUserGroup;
        set => SetProperty(ref _newUserGroup, value);
    }

    // Reset Password Dialog Props
    public bool IsResetPasswordDialogOpen
    {
        get => _isResetPasswordDialogOpen;
        set => SetProperty(ref _isResetPasswordDialogOpen, value);
    }

    public UiUserItem? SelectedUserForReset
    {
        get => _selectedUserForReset;
        set => SetProperty(ref _selectedUserForReset, value);
    }

    public string NewResetPassword
    {
        get => _newResetPassword;
        set => SetProperty(ref _newResetPassword, value);
    }

    // Create Group Dialog Props
    public bool IsCreateGroupDialogOpen
    {
        get => _isCreateGroupDialogOpen;
        set => SetProperty(ref _isCreateGroupDialogOpen, value);
    }

    public string NewGroupName
    {
        get => _newGroupName;
        set => SetProperty(ref _newGroupName, value);
    }

    // Add Member Dialog Props
    public bool IsAddMemberDialogOpen
    {
        get => _isAddMemberDialogOpen;
        set => SetProperty(ref _isAddMemberDialogOpen, value);
    }

    public UiGroupItem? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (SetProperty(ref _selectedGroup, value))
            {
                OnPropertyChanged(nameof(HasSelectedGroup));
            }
        }
    }

    public bool HasSelectedGroup => _selectedGroup != null;

    public int TotalUsersCount => Users.Count;
    public int ActiveUsersCount => Users.Count(u => !u.Disabled);
    public int DisabledUsersCount => Users.Count(u => u.Disabled);

    public string AddMemberUsername
    {
        get => _addMemberUsername;
        set => SetProperty(ref _addMemberUsername, value);
    }

    // Create Grant Dialog Props
    public bool IsCreateGrantDialogOpen
    {
        get => _isCreateGrantDialogOpen;
        set => SetProperty(ref _isCreateGrantDialogOpen, value);
    }

    public string GrantSubjectType
    {
        get => _grantSubjectType;
        set => SetProperty(ref _grantSubjectType, value);
    }

    public string GrantSubjectId
    {
        get => _grantSubjectId;
        set => SetProperty(ref _grantSubjectId, value);
    }

    public string GrantRoleId
    {
        get => _grantRoleId;
        set => SetProperty(ref _grantRoleId, value);
    }

    public string GrantPath
    {
        get => _grantPath;
        set => SetProperty(ref _grantPath, value);
    }

    // Commands
    public ICommand RefreshCommand { get; }
    public ICommand DismissStatusCommand { get; }

    // User Commands
    public ICommand OpenCreateUserDialogCommand { get; }
    public ICommand ConfirmCreateUserCommand { get; }
    public ICommand CancelCreateUserCommand { get; }

    public ICommand ToggleUserDisabledCommand { get; }
    public ICommand OpenResetPasswordDialogCommand { get; }
    public ICommand ConfirmResetPasswordCommand { get; }
    public ICommand CancelResetPasswordCommand { get; }

    // Group Commands
    public ICommand OpenCreateGroupDialogCommand { get; }
    public ICommand ConfirmCreateGroupCommand { get; }
    public ICommand CancelCreateGroupCommand { get; }

    public ICommand OpenAddMemberDialogCommand { get; }
    public ICommand ConfirmAddMemberCommand { get; }
    public ICommand CancelAddMemberCommand { get; }
    public ICommand RemoveMemberCommand { get; }

    // Grant Commands
    public ICommand OpenCreateGrantDialogCommand { get; }
    public ICommand ConfirmCreateGrantCommand { get; }
    public ICommand CancelCreateGrantCommand { get; }
    public ICommand RevokeGrantCommand { get; }

    public AdminPanelViewModel(IDfsClient client)
    {
        _client = client;

        RefreshCommand = new RelayCommand(async () => await LoadDataAsync());
        DismissStatusCommand = new RelayCommand(() => StatusMessage = "");

        // User
        OpenCreateUserDialogCommand = new RelayCommand(() =>
        {
            NewUsername = "";
            NewUserPassword = "";
            NewUserGroup = "Team A";
            IsCreateUserDialogOpen = true;
        });
        ConfirmCreateUserCommand = new RelayCommand(async () => await ExecuteCreateUserAsync());
        CancelCreateUserCommand = new RelayCommand(() => IsCreateUserDialogOpen = false);

        ToggleUserDisabledCommand = new RelayCommand(async param =>
        {
            if (param is UiUserItem user) await ExecuteToggleDisableAsync(user);
        });

        OpenResetPasswordDialogCommand = new RelayCommand(param =>
        {
            if (param is UiUserItem user)
            {
                SelectedUserForReset = user;
                NewResetPassword = "";
                IsResetPasswordDialogOpen = true;
            }
        });
        ConfirmResetPasswordCommand = new RelayCommand(async () => await ExecuteResetPasswordAsync());
        CancelResetPasswordCommand = new RelayCommand(() => IsResetPasswordDialogOpen = false);

        // Group
        OpenCreateGroupDialogCommand = new RelayCommand(() =>
        {
            NewGroupName = "";
            IsCreateGroupDialogOpen = true;
        });
        ConfirmCreateGroupCommand = new RelayCommand(async () => await ExecuteCreateGroupAsync());
        CancelCreateGroupCommand = new RelayCommand(() => IsCreateGroupDialogOpen = false);

        OpenAddMemberDialogCommand = new RelayCommand(param =>
        {
            if (param is UiGroupItem group)
            {
                SelectedGroup = group;
                AddMemberUsername = "";
                IsAddMemberDialogOpen = true;
            }
        });
        ConfirmAddMemberCommand = new RelayCommand(async () => await ExecuteAddMemberAsync());
        CancelAddMemberCommand = new RelayCommand(() => IsAddMemberDialogOpen = false);

        RemoveMemberCommand = new RelayCommand(async param =>
        {
            if (param is string memberUsername && SelectedGroup != null)
            {
                await ExecuteRemoveMemberAsync(SelectedGroup, memberUsername);
            }
        });

        // Grant
        OpenCreateGrantDialogCommand = new RelayCommand(() =>
        {
            GrantSubjectType = "User";
            GrantSubjectId = "tan.phan";
            GrantRoleId = BuiltInRoles.Editor;
            GrantPath = "/projects";
            IsCreateGrantDialogOpen = true;
        });
        ConfirmCreateGrantCommand = new RelayCommand(async () => await ExecuteCreateGrantAsync());
        CancelCreateGrantCommand = new RelayCommand(() => IsCreateGrantDialogOpen = false);

        RevokeGrantCommand = new RelayCommand(async param =>
        {
            if (param is UiGrantItem grant) await ExecuteRevokeGrantAsync(grant);
        });

        LoadFallbackSamples();
    }

    public async Task LoadDataAsync()
    {
        StatusMessage = "Đang đồng bộ dữ liệu quản trị từ Metadata Server...";
        StatusIsError = false;

        try
        {
            var users = await _client.ListUsersAsync();
            Users.Clear();
            foreach (var u in users) Users.Add(new UiUserItem(u));
        }
        catch (NotSupportedException)
        {
            // Demo client fallback
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi đồng bộ người dùng: {ex.Message}";
            StatusIsError = true;
            return;
        }

        try
        {
            var groups = await _client.ListGroupsAsync();
            Groups.Clear();
            foreach (var g in groups) Groups.Add(new UiGroupItem(g));
            if (SelectedGroup == null || !Groups.Contains(SelectedGroup))
            {
                SelectedGroup = Groups.FirstOrDefault();
            }
        }
        catch (NotSupportedException) { }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi đồng bộ nhóm: {ex.Message}";
            StatusIsError = true;
            return;
        }

        try
        {
            var grants = await _client.ListGrantsAsync();
            Grants.Clear();
            foreach (var g in grants) Grants.Add(new UiGrantItem(g));
        }
        catch (NotSupportedException) { }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi đồng bộ quyền: {ex.Message}";
            StatusIsError = true;
            return;
        }

        StatusMessage = "Đồng bộ dữ liệu Quản trị viên thành công.";
        StatusIsError = false;
    }

    private async Task ExecuteCreateUserAsync()
    {
        var username = NewUsername?.Trim();
        var password = NewUserPassword;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            SetStatus("Tên đăng nhập và mật khẩu không được để trống.", isError: true);
            return;
        }

        IsCreateUserDialogOpen = false;

        try
        {
            UserInfo created;
            try
            {
                created = await _client.CreateUserAsync(username, password);
            }
            catch (NotSupportedException)
            {
                created = new UserInfo($"u-{Guid.NewGuid():N}"[..8], username, false, new ValueList<string>([NewUserGroup]));
            }

            Users.Insert(0, new UiUserItem(created));
            SetStatus($"Đã tạo người dùng '{username}' thành công (Mật khẩu PBKDF2).", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus($"Lỗi tạo người dùng: {ex.Message}", isError: true);
        }
    }

    private async Task ExecuteToggleDisableAsync(UiUserItem user)
    {
        var targetState = !user.Disabled;
        try
        {
            try
            {
                await _client.SetUserDisabledAsync(user.Id, targetState);
            }
            catch (NotSupportedException) { }

            user.Disabled = targetState;
            SetStatus($"Đã {(targetState ? "khóa" : "kích hoạt lại")} tài khoản '{user.Username}'.", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus($"Lỗi khóa tài khoản: {ex.Message}", isError: true);
        }
    }

    private async Task ExecuteResetPasswordAsync()
    {
        if (SelectedUserForReset == null) return;
        var newPass = NewResetPassword;
        if (string.IsNullOrEmpty(newPass))
        {
            SetStatus("Mật khẩu mới không được để trống.", isError: true);
            return;
        }

        IsResetPasswordDialogOpen = false;
        var user = SelectedUserForReset;

        try
        {
            try
            {
                await _client.ResetPasswordAsync(user.Id, newPass);
            }
            catch (NotSupportedException) { }

            SetStatus($"Đã đặt lại mật khẩu cho '{user.Username}' thành công.", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus($"Lỗi đổi mật khẩu: {ex.Message}", isError: true);
        }
    }

    private async Task ExecuteCreateGroupAsync()
    {
        var groupName = NewGroupName?.Trim();
        if (string.IsNullOrWhiteSpace(groupName))
        {
            SetStatus("Tên nhóm không được để trống.", isError: true);
            return;
        }

        IsCreateGroupDialogOpen = false;

        try
        {
            GroupInfo created;
            try
            {
                created = await _client.CreateGroupAsync(groupName);
            }
            catch (NotSupportedException)
            {
                created = new GroupInfo($"g-{Guid.NewGuid():N}"[..8], groupName, new ValueList<string>());
            }

            Groups.Insert(0, new UiGroupItem(created));
            SetStatus($"Đã tạo nhóm '{groupName}' thành công.", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus($"Lỗi tạo nhóm: {ex.Message}", isError: true);
        }
    }

    private async Task ExecuteAddMemberAsync()
    {
        if (SelectedGroup == null) return;
        var username = AddMemberUsername?.Trim();
        if (string.IsNullOrWhiteSpace(username))
        {
            SetStatus("Tên người dùng không được để trống.", isError: true);
            return;
        }

        IsAddMemberDialogOpen = false;
        var grp = SelectedGroup;

        try
        {
            try
            {
                await _client.SetGroupMemberAsync(grp.Id, username, member: true);
            }
            catch (NotSupportedException) { }

            if (!grp.Members.Contains(username))
            {
                grp.Members.Add(username);
                grp.MemberCountText = $"{grp.Members.Count} thành viên";
            }
            SetStatus($"Đã thêm '{username}' vào nhóm '{grp.Name}'.", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus($"Lỗi thêm thành viên: {ex.Message}", isError: true);
        }
    }

    private async Task ExecuteRemoveMemberAsync(UiGroupItem grp, string username)
    {
        try
        {
            try
            {
                await _client.SetGroupMemberAsync(grp.Id, username, member: false);
            }
            catch (NotSupportedException) { }

            grp.Members.Remove(username);
            grp.MemberCountText = $"{grp.Members.Count} thành viên";
            SetStatus($"Đã xóa '{username}' khỏi nhóm '{grp.Name}'.", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus($"Lỗi xóa thành viên: {ex.Message}", isError: true);
        }
    }

    private async Task ExecuteCreateGrantAsync()
    {
        var subjectId = GrantSubjectId?.Trim();
        if (string.IsNullOrWhiteSpace(subjectId))
        {
            SetStatus("Đối tượng nhận quyền không được để trống.", isError: true);
            return;
        }

        IsCreateGrantDialogOpen = false;
        var subjectType = GrantSubjectType == "Group" ? SubjectType.Group : SubjectType.User;
        var path = string.IsNullOrWhiteSpace(GrantPath) ? null : GrantPath.Trim();

        try
        {
            GrantInfo created;
            try
            {
                created = await _client.CreateGrantAsync(GrantRoleId, subjectType, subjectId, path);
            }
            catch (NotSupportedException)
            {
                created = new GrantInfo(
                    Id: $"grant-{Guid.NewGuid():N}"[..8],
                    RoleId: GrantRoleId,
                    SubjectType: subjectType,
                    SubjectId: subjectId,
                    Path: path,
                    ItemId: null);
            }

            Grants.Insert(0, new UiGrantItem(created));
            SetStatus($"Đã cấp quyền '{GrantRoleId}' cho {subjectType} '{subjectId}' trên '{path ?? "Hệ thống"}' thành công.", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus($"Lỗi cấp quyền: {ex.Message}", isError: true);
        }
    }

    private async Task ExecuteRevokeGrantAsync(UiGrantItem grant)
    {
        try
        {
            try
            {
                await _client.RevokeGrantAsync(grant.Id);
            }
            catch (NotSupportedException) { }

            Grants.Remove(grant);
            SetStatus($"Đã thu hồi quyền '{grant.RoleId}' của '{grant.Subject}'.", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus($"Lỗi thu hồi quyền: {ex.Message}", isError: true);
        }
    }

    private void SetStatus(string msg, bool isError)
    {
        StatusMessage = msg;
        StatusIsError = isError;
    }

    private void LoadFallbackSamples()
    {
        Users.Clear();
        Users.Add(new UiUserItem("u-admin", "admin", false, "Admin, Team A"));
        Users.Add(new UiUserItem("u-an", "an.nguyen", false, "Team A"));
        Users.Add(new UiUserItem("u-tan", "tan.phan", false, "Team A"));
        Users.Add(new UiUserItem("u-guest", "guest_user", true, "Guests"));
        Users.Add(new UiUserItem("u-temp", "temp_tester", false, "Guests"));

        Groups.Clear();
        var teamA = new UiGroupItem("g-teama", "Team A", 3);
        teamA.Members.Add("an.nguyen");
        teamA.Members.Add("tan.phan");
        teamA.Members.Add("admin");
        Groups.Add(teamA);

        var guests = new UiGroupItem("g-guests", "Guests", 2);
        guests.Members.Add("guest_user");
        guests.Members.Add("temp_tester");
        Groups.Add(guests);

        var auditors = new UiGroupItem("g-auditors", "Auditors", 1);
        auditors.Members.Add("cuong.khong");
        Groups.Add(auditors);

        SelectedGroup = teamA;

        Grants.Clear();
        Grants.Add(new UiGrantItem("grant-01", BuiltInRoles.Owner, "User: an.nguyen", "/projects", true, "Trực tiếp"));
        Grants.Add(new UiGrantItem("grant-02", BuiltInRoles.Editor, "Group: Team A", "/projects", true, "Trực tiếp"));
        Grants.Add(new UiGrantItem("grant-03", BuiltInRoles.Viewer, "Group: Guests", "/projects", true, "Trực tiếp"));
        Grants.Add(new UiGrantItem("grant-04", BuiltInRoles.Editor, "Group: Team A", "/projects/dfs", true, "Kế thừa từ /projects"));
    }
}
