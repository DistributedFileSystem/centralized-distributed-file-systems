using Dfs.Shared.Messages;
using Dfs.Shared.Metadata;
using Dfs.Shared.Security;

namespace Dfs.MetadataServer.Security;

/// <summary>Evaluates system and inherited item grants without writing to the store.</summary>
public sealed class RbacAuthorizer(IMetadataStore store) : IAuthorizer
{
    private static readonly HashSet<string> SystemPermissions =
    [
        Permission.UserManage, Permission.GroupManage, Permission.RoleManage,
        Permission.ClusterView, Permission.ClusterManage, Permission.AuditView,
    ];

    private static readonly HashSet<string> ItemPermissions =
    [
        Permission.ItemList, Permission.FileRead, Permission.FileWrite,
        Permission.ItemCreate, Permission.ItemRename, Permission.ItemDelete,
        Permission.ItemShare, Permission.VersionManage,
    ];

    /// <inheritdoc />
    public async Task<AuthorizationResult> AuthorizeAsync(string userId, string permission, string? itemId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(permission))
            return AuthorizationResult.Deny("Missing user or permission.");
        var isSystem = SystemPermissions.Contains(permission);
        if ((!isSystem && !ItemPermissions.Contains(permission)) || (isSystem != (itemId is null)))
            return AuthorizationResult.Deny("Permission scope is invalid.");

        var user = await store.FindUserByIdAsync(userId, cancellationToken);
        if (user is null || user.Disabled)
            return AuthorizationResult.Deny("User does not exist or is disabled.");

        ItemInfo? item = null;
        if (!isSystem)
        {
            item = await store.FindItemByIdAsync(itemId!, cancellationToken);
            if (item is null) return AuthorizationResult.Deny("Item does not exist.");
        }

        var groupIds = (await store.ListGroupsAsync(cancellationToken))
            .Where(group => group.MemberIds.Contains(userId))
            .Select(group => group.Id).ToHashSet(StringComparer.Ordinal);
        var roles = (await store.ListRolesAsync(cancellationToken)).ToDictionary(role => role.Id, StringComparer.Ordinal);
        var grants = await store.ListGrantsAsync(null, null, false, cancellationToken);

        bool BelongsToUser(GrantInfo grant) => grant.SubjectType switch
        {
            SubjectType.User => grant.SubjectId == userId,
            SubjectType.Group => groupIds.Contains(grant.SubjectId),
            _ => false,
        };

        foreach (var grant in grants.Where(g => g.ItemId is null && BelongsToUser(g)))
        {
            if (grant.RoleId == BuiltInRoles.AdminId && roles.ContainsKey(BuiltInRoles.AdminId))
                return AuthorizationResult.Allow("Admin system grant.");
            if (isSystem && roles.TryGetValue(grant.RoleId, out var role) &&
                role.Scope == RoleScope.System && role.Permissions.Contains(permission))
                return AuthorizationResult.Allow($"System grant for role {role.Name}.");
        }

        if (isSystem) return AuthorizationResult.Deny("No matching system grant.");

        var ancestorIds = new HashSet<string>(StringComparer.Ordinal);
        var currentPath = item!.Path;
        while (true)
        {
            var ancestor = await store.FindItemByPathAsync(currentPath, cancellationToken);
            if (ancestor is null) break;
            ancestorIds.Add(ancestor.Id);
            var separator = currentPath.LastIndexOf('/');
            if (separator <= 0) break;
            currentPath = currentPath[..separator];
        }

        foreach (var grant in grants.Where(g => g.ItemId is not null &&
                     ancestorIds.Contains(g.ItemId) && BelongsToUser(g)))
        {
            if (roles.TryGetValue(grant.RoleId, out var role) &&
                role.Scope == RoleScope.Item && role.Permissions.Contains(permission))
                return AuthorizationResult.Allow($"Inherited item grant for role {role.Name}.");
        }

        return AuthorizationResult.Deny("No matching item grant.");
    }
}
