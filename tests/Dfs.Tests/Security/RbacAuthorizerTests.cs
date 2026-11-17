using Dfs.MetadataServer.Security;
using Dfs.MetadataServer.Store;
using Dfs.Shared.Messages;
using Dfs.Shared.Security;
using CreateFolderCommand = Dfs.Shared.Commands.CreateFolder;
using CreateGroupCommand = Dfs.Shared.Commands.CreateGroup;
using CreateRoleCommand = Dfs.Shared.Commands.CreateRole;
using CreateUserCommand = Dfs.Shared.Commands.CreateUser;
using RevokeGrantCommand = Dfs.Shared.Commands.RevokeGrant;
using SetGrantCommand = Dfs.Shared.Commands.SetGrant;
using SetGroupMemberCommand = Dfs.Shared.Commands.SetGroupMember;
using SetUserDisabledCommand = Dfs.Shared.Commands.SetUserDisabled;
using UpdateRoleCommand = Dfs.Shared.Commands.UpdateRole;

namespace Dfs.Tests.Security;

public sealed class RbacAuthorizerTests : IDisposable
{
    private static readonly DateTime At = new(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"dfs-rbac-{Guid.NewGuid():N}");

    private MetadataStore Open() => new(Path.Combine(_directory, "oplog.jsonl"), Path.Combine(_directory, "metadata.db"));

    [Fact]
    public async Task Built_in_roles_are_seeded_once_with_fixed_permissions_and_cannot_be_updated()
    {
        using var store = Open();
        var seeder = new BuiltInRoleSeeder(store);
        await seeder.SeedAsync();
        var roles = await store.ListRolesAsync();

        Assert.Equal(5, roles.Count);
        Assert.All(roles, role => Assert.True(role.BuiltIn));
        Assert.Equal(new[] { Permission.AuditView, Permission.ClusterView },
            roles.Single(role => role.Id == BuiltInRoles.AuditorId).Permissions);
        Assert.Contains(Permission.VersionManage,
            roles.Single(role => role.Id == BuiltInRoles.OwnerId).Permissions);
        Assert.DoesNotContain(Permission.ItemShare,
            roles.Single(role => role.Id == BuiltInRoles.EditorId).Permissions);
        Assert.Equal(new[] { Permission.FileRead, Permission.ItemList },
            roles.Single(role => role.Id == BuiltInRoles.ViewerId).Permissions);

        var linesBefore = File.ReadAllLines(Path.Combine(_directory, "oplog.jsonl")).Length;
        await seeder.SeedAsync();
        Assert.Equal(linesBefore, File.ReadAllLines(Path.Combine(_directory, "oplog.jsonl")).Length);
        var update = await store.SubmitAsync(new UpdateRoleCommand(BuiltInRoles.ViewerId,
            new ValueList<string>([Permission.FileWrite]), At));
        Assert.False(update.Success);
        Assert.Equal(ErrorCode.BadRequest, update.Error);
    }

    [Fact]
    public async Task Owner_and_group_grants_inherit_to_descendants_but_not_to_ancestors()
    {
        using var store = Open();
        await new BuiltInRoleSeeder(store).SeedAsync();
        await AddUser(store, "alice");
        await AddUser(store, "bob");
        await AddUser(store, "carol");
        await Submit(store, new CreateFolderCommand("projects", "root", "projects", "alice", At));
        await Submit(store, new CreateFolderCommand("docs", "projects", "docs", "bob", At));
        await Submit(store, new CreateFolderCommand("leaf", "docs", "leaf", "bob", At));

        var ownerGrant = Assert.Single(await store.ListGrantsAsync("projects", "alice", false));
        Assert.Equal(BuiltInRoles.OwnerId, ownerGrant.RoleId);
        Assert.Equal("/projects", ownerGrant.Path);
        var authorizer = new RbacAuthorizer(store);
        Assert.True((await authorizer.AuthorizeAsync("alice", Permission.FileWrite, "leaf")).Allowed);
        Assert.False((await authorizer.AuthorizeAsync("carol", Permission.FileRead, "leaf")).Allowed);

        await Submit(store, new CreateGroupCommand("team", "Team", At));
        await Submit(store, new SetGroupMemberCommand("team", "carol", true, At));
        await Submit(store, new SetGrantCommand("viewer-team", BuiltInRoles.ViewerId,
            SubjectType.Group, "team", "projects", At));
        Assert.Contains("team", (await store.ListUsersAsync()).Single(u => u.Id == "carol").GroupIds);
        Assert.Contains("carol", (await store.ListGroupsAsync()).Single(g => g.Id == "team").MemberIds);
        Assert.True((await authorizer.AuthorizeAsync("carol", Permission.FileRead, "leaf")).Allowed);
        Assert.False((await authorizer.AuthorizeAsync("carol", Permission.FileWrite, "leaf")).Allowed);

        await Submit(store, new SetGrantCommand("editor-carol", BuiltInRoles.EditorId,
            SubjectType.User, "carol", "docs", At));
        Assert.True((await authorizer.AuthorizeAsync("carol", Permission.FileWrite, "leaf")).Allowed);
        Assert.False((await authorizer.AuthorizeAsync("carol", Permission.FileWrite, "projects")).Allowed);

        await Submit(store, new RevokeGrantCommand("editor-carol", At));
        await Submit(store, new SetGroupMemberCommand("team", "carol", false, At));
        Assert.False((await authorizer.AuthorizeAsync("carol", Permission.FileRead, "leaf")).Allowed);

        await Submit(store, new RevokeGrantCommand(ownerGrant.Id, At));
        Assert.False((await authorizer.AuthorizeAsync("alice", Permission.FileWrite, "leaf")).Allowed);
    }

    [Fact]
    public async Task Group_system_grant_is_included_in_login_permissions()
    {
        using var store = Open();
        await new BuiltInRoleSeeder(store).SeedAsync();
        Assert.True((await new UserCredentialService(store).CreateUserAsync("bob", "bob", "secret", At)).Success);
        await Submit(store, new CreateGroupCommand("auditors", "Auditors", At));
        await Submit(store, new SetGroupMemberCommand("auditors", "bob", true, At));
        await Submit(store, new SetGrantCommand("audit-team", BuiltInRoles.AuditorId,
            SubjectType.Group, "auditors", null, At));

        var response = await new SessionService(store).LoginAsync(new Login("login", "bob", "secret"));
        Assert.Contains(BuiltInRoles.Auditor, response.SystemRoles);
        Assert.Contains(Permission.AuditView, response.Permissions);
        Assert.DoesNotContain(Permission.UserManage, response.Permissions);
    }

    [Fact]
    public async Task System_grants_apply_to_user_or_group_and_disabled_users_are_denied()
    {
        using var store = Open();
        await new BuiltInRoleSeeder(store).SeedAsync();
        await AddUser(store, "alice");
        await AddUser(store, "bob");
        await Submit(store, new CreateFolderCommand("folder", "root", "folder", "alice", At));
        await Submit(store, new CreateGroupCommand("auditors", "Auditors", At));
        await Submit(store, new SetGroupMemberCommand("auditors", "bob", true, At));
        await Submit(store, new SetGrantCommand("audit-team", BuiltInRoles.AuditorId,
            SubjectType.Group, "auditors", null, At));
        var authorizer = new RbacAuthorizer(store);

        Assert.True((await authorizer.AuthorizeAsync("bob", Permission.ClusterView, null)).Allowed);
        Assert.False((await authorizer.AuthorizeAsync("bob", Permission.UserManage, null)).Allowed);
        Assert.False((await authorizer.AuthorizeAsync("bob", Permission.FileRead, "folder")).Allowed);

        await Submit(store, new SetGrantCommand("admin-bob", BuiltInRoles.AdminId,
            SubjectType.User, "bob", null, At));
        Assert.True((await authorizer.AuthorizeAsync("bob", Permission.UserManage, null)).Allowed);
        Assert.True((await authorizer.AuthorizeAsync("bob", Permission.FileWrite, "folder")).Allowed);
        Assert.False((await authorizer.AuthorizeAsync("bob", Permission.FileRead, "missing")).Allowed);

        await Submit(store, new SetUserDisabledCommand("bob", true, At));
        Assert.False((await authorizer.AuthorizeAsync("bob", Permission.ClusterView, null)).Allowed);
        Assert.False((await authorizer.AuthorizeAsync("bob", Permission.FileWrite, "folder")).Allowed);
    }

    [Fact]
    public async Task Custom_role_updates_and_group_grants_survive_store_restart()
    {
        using (var store = Open())
        {
            await new BuiltInRoleSeeder(store).SeedAsync();
            await AddUser(store, "alice");
            await AddUser(store, "bob");
            await Submit(store, new CreateFolderCommand("folder", "root", "folder", "alice", At));
            await Submit(store, new CreateGroupCommand("team", "Team", At));
            await Submit(store, new SetGroupMemberCommand("team", "bob", true, At));
            await Submit(store, new CreateRoleCommand("custom", "Reader", RoleScope.Item,
                new ValueList<string>([Permission.ItemList]), At));
            await Submit(store, new SetGrantCommand("custom-grant", "custom", SubjectType.Group,
                "team", "folder", At));
            Assert.False((await new RbacAuthorizer(store).AuthorizeAsync("bob", Permission.FileRead, "folder")).Allowed);
            await Submit(store, new UpdateRoleCommand("custom", new ValueList<string>([Permission.FileRead]), At));
        }

        File.Delete(Path.Combine(_directory, "metadata.db"));
        using var reopened = Open();
        Assert.Equal(new[] { "team" }, (await reopened.ListUsersAsync()).Single(u => u.Id == "bob").GroupIds);
        Assert.True((await new RbacAuthorizer(reopened).AuthorizeAsync("bob", Permission.FileRead, "folder")).Allowed);
        Assert.False((await new RbacAuthorizer(reopened).AuthorizeAsync("bob", Permission.ItemList, "folder")).Allowed);
        Assert.Equal(5, (await reopened.ListRolesAsync()).Count(role => role.BuiltIn));
    }

    private static async Task AddUser(MetadataStore store, string name) =>
        await Submit(store, new CreateUserCommand(name, name, "hash", "salt", At));

    private static async Task Submit(MetadataStore store, Dfs.Shared.Commands.IMetadataCommand command) =>
        Assert.True((await store.SubmitAsync(command)).Success);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
