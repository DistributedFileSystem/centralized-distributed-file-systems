namespace Dfs.Shared.Security;

/// <summary>Permission names, as in docs/rbac.md.</summary>
public static class Permission
{
    /// <summary>Manage users (system).</summary>
    public const string UserManage = "user.manage";
    /// <summary>Manage groups (system).</summary>
    public const string GroupManage = "group.manage";
    /// <summary>Manage roles and system grants (system).</summary>
    public const string RoleManage = "role.manage";
    /// <summary>View Data Servers (system).</summary>
    public const string ClusterView = "cluster.view";
    /// <summary>Manage the cluster (system).</summary>
    public const string ClusterManage = "cluster.manage";
    /// <summary>View the audit log (system).</summary>
    public const string AuditView = "audit.view";

    /// <summary>List an item (folder or file).</summary>
    public const string ItemList = "item.list";
    /// <summary>Read a file.</summary>
    public const string FileRead = "file.read";
    /// <summary>Save a new version of a file.</summary>
    public const string FileWrite = "file.write";
    /// <summary>Create an item in a folder.</summary>
    public const string ItemCreate = "item.create";
    /// <summary>Rename or move an item.</summary>
    public const string ItemRename = "item.rename";
    /// <summary>Delete an item.</summary>
    public const string ItemDelete = "item.delete";
    /// <summary>Grant roles on an item.</summary>
    public const string ItemShare = "item.share";
    /// <summary>Manage versions (reserved, no message uses it in stage 1).</summary>
    public const string VersionManage = "version.manage";
}
