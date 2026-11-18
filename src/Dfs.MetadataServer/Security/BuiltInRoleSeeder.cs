using Dfs.Shared.Messages;
using Dfs.Shared.Metadata;
using Dfs.Shared.Security;
using CreateRole = Dfs.Shared.Commands.CreateRole;

namespace Dfs.MetadataServer.Security;

/// <summary>Creates the fixed RBAC roles through the metadata command boundary.</summary>
public sealed class BuiltInRoleSeeder(IMetadataStore store, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <summary>Seeds missing roles without changing existing definitions on restart.</summary>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var existing = await store.ListRolesAsync(cancellationToken);
        foreach (var definition in Definitions(_time.GetUtcNow().UtcDateTime))
        {
            var role = existing.FirstOrDefault(r => r.Id == definition.RoleId);
            if (role is not null)
            {
                if (role.Name != definition.Name || role.Scope != definition.Scope ||
                    !role.Permissions.ToHashSet(StringComparer.Ordinal).SetEquals(definition.Permissions))
                    throw new InvalidOperationException($"Built-in role '{definition.RoleId}' has an unexpected definition.");
                continue;
            }

            var result = await store.SubmitAsync(definition, cancellationToken);
            if (!result.Success)
                throw new InvalidOperationException($"Could not seed built-in role '{definition.RoleId}': {result.Message}");
        }
    }

    internal static IReadOnlyList<CreateRole> Definitions(DateTime at) =>
    [
        new(BuiltInRoles.AdminId, BuiltInRoles.Admin, RoleScope.System,
            new ValueList<string>([Permission.UserManage, Permission.GroupManage, Permission.RoleManage,
                Permission.ClusterView, Permission.ClusterManage, Permission.AuditView]), at),
        new(BuiltInRoles.AuditorId, BuiltInRoles.Auditor, RoleScope.System,
            new ValueList<string>([Permission.ClusterView, Permission.AuditView]), at),
        new(BuiltInRoles.OwnerId, BuiltInRoles.Owner, RoleScope.Item,
            new ValueList<string>([Permission.ItemList, Permission.FileRead, Permission.FileWrite,
                Permission.ItemCreate, Permission.ItemRename, Permission.ItemDelete,
                Permission.ItemShare, Permission.VersionManage]), at),
        new(BuiltInRoles.EditorId, BuiltInRoles.Editor, RoleScope.Item,
            new ValueList<string>([Permission.ItemList, Permission.FileRead, Permission.FileWrite,
                Permission.ItemCreate, Permission.ItemRename, Permission.ItemDelete]), at),
        new(BuiltInRoles.ViewerId, BuiltInRoles.Viewer, RoleScope.Item,
            new ValueList<string>([Permission.ItemList, Permission.FileRead]), at),
    ];
}
