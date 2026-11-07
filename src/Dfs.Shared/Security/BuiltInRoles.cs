namespace Dfs.Shared.Security;

/// <summary>Names and fixed ids of the built-in roles, as in docs/rbac.md.</summary>
public static class BuiltInRoles
{
    /// <summary>Name of the system role with all permissions.</summary>
    public const string Admin = "Admin";
    /// <summary>Name of the system role with cluster.view and audit.view.</summary>
    public const string Auditor = "Auditor";
    /// <summary>Name of the item role with all item permissions.</summary>
    public const string Owner = "Owner";
    /// <summary>Name of the item role with list, read, write, create, rename, delete.</summary>
    public const string Editor = "Editor";
    /// <summary>Name of the item role with list and read.</summary>
    public const string Viewer = "Viewer";

    /// <summary>Fixed id of <see cref="Admin"/>.</summary>
    public const string AdminId = "r-admin";
    /// <summary>Fixed id of <see cref="Auditor"/>.</summary>
    public const string AuditorId = "r-auditor";
    /// <summary>Fixed id of <see cref="Owner"/>.</summary>
    public const string OwnerId = "r-owner";
    /// <summary>Fixed id of <see cref="Editor"/>.</summary>
    public const string EditorId = "r-editor";
    /// <summary>Fixed id of <see cref="Viewer"/>.</summary>
    public const string ViewerId = "r-viewer";
}
