using System.Linq;
using System.Threading.Tasks;
using Dfs.Client.Desktop.Models;
using Dfs.Client.Desktop.ViewModels;
using Dfs.Fakes;
using Dfs.Shared.Security;
using Xunit;

namespace Dfs.Tests.Client;

public class AdminPanelViewModelTests
{
    private readonly FakeDfsClient _client = new();

    [Fact]
    public void AdminPanelViewModel_InitialState_LoadsFallbackSamples()
    {
        var vm = new AdminPanelViewModel(_client);

        Assert.NotEmpty(vm.Users);
        Assert.NotEmpty(vm.Groups);
        Assert.NotEmpty(vm.Grants);
        Assert.NotNull(vm.SelectedGroup);
        Assert.True(vm.TotalUsersCount > 0);
        Assert.True(vm.IsUsersTabSelected);
        Assert.False(vm.IsGroupsTabSelected);
        Assert.False(vm.IsGrantsTabSelected);
    }

    [Fact]
    public void AdminPanelViewModel_SearchUserQuery_FiltersUsers()
    {
        var vm = new AdminPanelViewModel(_client);
        Assert.Equal(vm.Users.Count, vm.FilteredUsers.Count());

        vm.SearchUserQuery = "tan";
        var filtered = vm.FilteredUsers.ToList();

        Assert.Single(filtered);
        Assert.Equal("tan.phan", filtered[0].Username);

        vm.SearchUserQuery = "nonexistent_user_xyz";
        Assert.Empty(vm.FilteredUsers);
    }

    [Fact]
    public void AdminPanelViewModel_SearchGroupQuery_FiltersGroups()
    {
        var vm = new AdminPanelViewModel(_client);
        Assert.Equal(vm.Groups.Count, vm.FilteredGroups.Count());

        vm.SearchGroupQuery = "Guests";
        var filtered = vm.FilteredGroups.ToList();

        Assert.Single(filtered);
        Assert.Equal("Guests", filtered[0].Name);
    }

    [Fact]
    public void AdminPanelViewModel_SelectedTabIndex_ChangesTabSelection()
    {
        var vm = new AdminPanelViewModel(_client);

        vm.SelectedTabIndex = 1;
        Assert.False(vm.IsUsersTabSelected);
        Assert.True(vm.IsGroupsTabSelected);
        Assert.False(vm.IsGrantsTabSelected);

        vm.SelectedTabIndex = 2;
        Assert.False(vm.IsUsersTabSelected);
        Assert.False(vm.IsGroupsTabSelected);
        Assert.True(vm.IsGrantsTabSelected);
    }

    [Fact]
    public void AdminPanelViewModel_CreateUserCommand_AddsUser()
    {
        var vm = new AdminPanelViewModel(_client);
        var initialCount = vm.Users.Count;

        vm.OpenCreateUserDialogCommand.Execute(null);
        Assert.True(vm.IsCreateUserDialogOpen);

        vm.NewUsername = "new_operator";
        vm.NewUserPassword = "Password123!";
        vm.NewUserGroup = "Team A";

        vm.ConfirmCreateUserCommand.Execute(null);

        Assert.False(vm.IsCreateUserDialogOpen);
        Assert.Equal(initialCount + 1, vm.Users.Count);
        Assert.Contains(vm.Users, u => u.Username == "new_operator");
        Assert.False(vm.StatusIsError);
    }

    [Fact]
    public void AdminPanelViewModel_CreateUserCommand_EmptyCredentials_SetsErrorStatus()
    {
        var vm = new AdminPanelViewModel(_client);
        var initialCount = vm.Users.Count;

        vm.OpenCreateUserDialogCommand.Execute(null);
        vm.NewUsername = "";
        vm.NewUserPassword = "";

        vm.ConfirmCreateUserCommand.Execute(null);

        Assert.Equal(initialCount, vm.Users.Count);
        Assert.True(vm.StatusIsError);
        Assert.Contains("không được để trống", vm.StatusMessage);
    }

    [Fact]
    public void AdminPanelViewModel_ToggleUserDisabledCommand_TogglesState()
    {
        var vm = new AdminPanelViewModel(_client);
        var activeUser = vm.Users.First(u => !u.Disabled);

        vm.ToggleUserDisabledCommand.Execute(activeUser);

        Assert.True(activeUser.Disabled);
        Assert.False(vm.StatusIsError);

        vm.ToggleUserDisabledCommand.Execute(activeUser);
        Assert.False(activeUser.Disabled);
    }

    [Fact]
    public void AdminPanelViewModel_CreateGroupCommand_AddsGroup()
    {
        var vm = new AdminPanelViewModel(_client);
        var initialCount = vm.Groups.Count;

        vm.OpenCreateGroupDialogCommand.Execute(null);
        Assert.True(vm.IsCreateGroupDialogOpen);

        vm.NewGroupName = "DevOps";
        vm.ConfirmCreateGroupCommand.Execute(null);

        Assert.False(vm.IsCreateGroupDialogOpen);
        Assert.Equal(initialCount + 1, vm.Groups.Count);
        Assert.Equal("DevOps", vm.Groups[0].Name);
    }

    [Fact]
    public void AdminPanelViewModel_AddAndRemoveMember_UpdatesGroup()
    {
        var vm = new AdminPanelViewModel(_client);
        var group = vm.Groups.First();
        vm.SelectedGroup = group;

        vm.OpenAddMemberDialogCommand.Execute(group);
        Assert.True(vm.IsAddMemberDialogOpen);

        vm.AddMemberUsername = "john.doe";
        vm.ConfirmAddMemberCommand.Execute(null);

        Assert.False(vm.IsAddMemberDialogOpen);
        Assert.Contains("john.doe", group.Members);

        vm.RemoveMemberCommand.Execute("john.doe");
        Assert.DoesNotContain("john.doe", group.Members);
    }

    [Fact]
    public void AdminPanelViewModel_CreateGrantAndRevoke_UpdatesGrantsList()
    {
        var vm = new AdminPanelViewModel(_client);
        var initialCount = vm.Grants.Count;

        vm.OpenCreateGrantDialogCommand.Execute(null);
        Assert.True(vm.IsCreateGrantDialogOpen);

        vm.GrantSubjectType = "User";
        vm.GrantSubjectId = "tan.phan";
        vm.GrantRoleId = BuiltInRoles.Admin;
        vm.GrantPath = "/system";

        vm.ConfirmCreateGrantCommand.Execute(null);

        Assert.False(vm.IsCreateGrantDialogOpen);
        Assert.Equal(initialCount + 1, vm.Grants.Count);
        var addedGrant = vm.Grants[0];
        Assert.Equal(BuiltInRoles.Admin, addedGrant.RoleId);

        vm.RevokeGrantCommand.Execute(addedGrant);
        Assert.Equal(initialCount, vm.Grants.Count);
        Assert.DoesNotContain(addedGrant, vm.Grants);
    }
}
