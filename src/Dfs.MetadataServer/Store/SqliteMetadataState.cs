using System.Globalization;
using Dfs.MetadataServer.Handlers;
using Dfs.Shared.Commands;
using Dfs.Shared.Messages;
using Dfs.Shared.Metadata;
using Dfs.Shared.Security;
using CreateFolder = Dfs.Shared.Commands.CreateFolder;
using CreateUser = Dfs.Shared.Commands.CreateUser;
using DeleteItem = Dfs.Shared.Commands.DeleteItem;
using MoveItem = Dfs.Shared.Commands.MoveItem;
using RenameItem = Dfs.Shared.Commands.RenameItem;
using CreateRole = Dfs.Shared.Commands.CreateRole;
using SetUserDisabled = Dfs.Shared.Commands.SetUserDisabled;
using Microsoft.Data.Sqlite;

namespace Dfs.MetadataServer.Store;

/// <summary>SQLite state derived from the op log.</summary>
internal sealed class SqliteMetadataState : IDisposable
{
    /// <summary>Id of the root folder; top-level items have this parent.</summary>
    public const string RootId = "root";

    private const string TimeFormat = "yyyy-MM-ddTHH:mm:ss.FFFFFFFZ";
    private const string ItemColumns = "id, parent_id, name, kind, owner_id, current_version, size, modified_at";

    private readonly SqliteConnection _db;

    public SqliteMetadataState(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        _db.Open();
        MetadataSchema.Initialize(_db);
    }

    /// <summary>Gets the seq of the last applied op log line.</summary>
    public long AppliedSeq() => (long)Scalar("SELECT seq FROM applied_seq WHERE id = 1")!;

    /// <summary>Checks a command against the current state; returns a failed result or null when it is acceptable.</summary>
    public CommandResult? Validate(IMetadataCommand command) => command switch
    {
        CreateFolder c => ValidateFolder(c),
        RenameItem c => ValidateRename(c),
        MoveItem c => ValidateMove(c),
        DeleteItem c => ValidateDelete(c),
        CreateUser c => Scalar("SELECT 1 FROM users WHERE username = $v OR id = $i", ("$v", c.Username), ("$i", c.UserId)) is not null
            ? CommandResult.Fail(ErrorCode.AlreadyExists, $"User '{c.Username}' already exists.")
            : null,
        SetUserDisabled c => Scalar("SELECT 1 FROM users WHERE id = $id", ("$id", c.UserId)) is null
            ? CommandResult.Fail(ErrorCode.NotFound, "User not found.") : null,
        CreateRole c => ValidateRole(c),
        SetGrant c => ValidateGrant(c),
        CreateSession c => ValidateSession(c),
        DeleteSession => null,
        _ => CommandResult.Fail(ErrorCode.BadRequest, $"Unsupported command {command.GetType().Name}."),
    };

    /// <summary>Applies a logged command and records its seq, in one transaction.</summary>
    public void Apply(long seq, IMetadataCommand command)
    {
        using var tx = _db.BeginTransaction();
        switch (command)
        {
            case CreateFolder c:
                Exec(
                    "INSERT INTO items (id, parent_id, name, kind, owner_id, current_version, size, modified_at) VALUES ($id, $p, $n, 'Folder', $o, 0, 0, $t)",
                    ("$id", c.ItemId), ("$p", c.ParentId), ("$n", c.Name), ("$o", c.OwnerId), ("$t", Format(c.At)));
                break;
            case RenameItem c:
                Exec(
                    "UPDATE items SET name = $n, modified_at = $t WHERE id = $id",
                    ("$n", c.NewName), ("$t", Format(c.At)), ("$id", c.ItemId));
                break;
            case MoveItem c:
                Exec(
                    "UPDATE items SET parent_id = $p, modified_at = $t WHERE id = $id",
                    ("$p", c.NewParentId), ("$t", Format(c.At)), ("$id", c.ItemId));
                break;
            case DeleteItem c:
                DeleteSubtree(c.ItemId);
                break;
            case CreateUser c:
                Exec(
                    "INSERT INTO users (id, username, password_hash, salt, disabled, created_at) VALUES ($id, $u, $h, $s, 0, $t)",
                    ("$id", c.UserId), ("$u", c.Username), ("$h", c.PasswordHash), ("$s", c.Salt), ("$t", Format(c.At)));
                break;
            case SetUserDisabled c:
                Exec("UPDATE users SET disabled = $disabled WHERE id = $id", ("$disabled", c.Disabled ? 1 : 0), ("$id", c.UserId));
                break;
            case CreateRole c:
                Exec("INSERT INTO roles (id, name, scope) VALUES ($id, $name, $scope)",
                    ("$id", c.RoleId), ("$name", c.Name), ("$scope", c.Scope.ToString()));
                foreach (var permission in c.Permissions)
                    Exec("INSERT INTO role_permissions (role_id, permission) VALUES ($id, $permission)",
                        ("$id", c.RoleId), ("$permission", permission));
                break;
            case SetGrant c:
                Exec("INSERT INTO grants (id, role_id, subject_type, subject_id, item_id) VALUES ($id, $role, $type, $subject, $item)",
                    ("$id", c.GrantId), ("$role", c.RoleId), ("$type", c.SubjectType.ToString()),
                    ("$subject", c.SubjectId), ("$item", (object?)c.ItemId ?? DBNull.Value));
                break;
            case CreateSession c:
                Exec("INSERT INTO sessions (token, user_id, expires_at) VALUES ($token, $user, $expiry)",
                    ("$token", c.SessionToken), ("$user", c.UserId), ("$expiry", Format(c.ExpiresAt)));
                break;
            case DeleteSession c:
                Exec("DELETE FROM sessions WHERE token = $token", ("$token", c.SessionToken));
                break;
            default:
                throw new InvalidOperationException($"Unsupported command {command.GetType().Name}.");
        }
        Exec("UPDATE applied_seq SET seq = $s WHERE id = 1", ("$s", seq));
        tx.Commit();
    }

    public ItemInfo? FindItemById(string itemId) => ReadItems($"SELECT {ItemColumns} FROM items WHERE id = $v", ("$v", itemId)).FirstOrDefault();

    public ItemInfo? FindItemByPath(string path)
    {
        var current = RootId;
        ItemInfo? item = null;
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            item = ReadItems($"SELECT {ItemColumns} FROM items WHERE parent_id = $p AND name = $n", ("$p", current), ("$n", part)).FirstOrDefault();
            if (item is null) return null;
            current = item.Id;
        }
        return item;
    }

    public IReadOnlyList<ItemInfo> ListChildren(string folderId) =>
        ReadItems($"SELECT {ItemColumns} FROM items WHERE parent_id = $p ORDER BY name", ("$p", folderId));

    public UserCredentials? FindUserByName(string username) => ReadUser("username", username);

    public UserCredentials? FindUserById(string userId) => ReadUser("id", userId);

    public IReadOnlyList<UserInfo> ListUsers()
    {
        var list = new List<UserInfo>();
        using var cmd = Command("SELECT id, username, disabled FROM users ORDER BY username");
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new UserInfo(r.GetString(0), r.GetString(1), r.GetInt64(2) != 0, new ValueList<string>()));
        return list;
    }

    public SessionRecord? FindSession(string token)
    {
        using var cmd = Command("SELECT token, user_id, expires_at FROM sessions WHERE token = $token", ("$token", token));
        using var r = cmd.ExecuteReader();
        return r.Read() ? new SessionRecord(r.GetString(0), r.GetString(1), Parse(r.GetString(2))) : null;
    }

    public IReadOnlyList<RoleInfo> ListRoles()
    {
        var roles = new List<RoleInfo>();
        using var cmd = Command("SELECT id, name, scope FROM roles ORDER BY name");
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var id = r.GetString(0);
            var permissions = new List<string>();
            using var permissionCmd = Command("SELECT permission FROM role_permissions WHERE role_id = $id ORDER BY permission", ("$id", id));
            using var permissionReader = permissionCmd.ExecuteReader();
            while (permissionReader.Read()) permissions.Add(permissionReader.GetString(0));
            var builtIn = id is BuiltInRoles.AdminId or BuiltInRoles.AuditorId or BuiltInRoles.OwnerId or BuiltInRoles.EditorId or BuiltInRoles.ViewerId;
            roles.Add(new RoleInfo(id, r.GetString(1), Enum.Parse<RoleScope>(r.GetString(2)), builtIn, new ValueList<string>(permissions)));
        }
        return roles;
    }

    public IReadOnlyList<GrantInfo> ListGrants(string? itemId, string? subjectId, bool systemOnly)
    {
        var grants = new List<GrantInfo>();
        var sql = "SELECT id, role_id, subject_type, subject_id, item_id FROM grants WHERE 1 = 1";
        if (systemOnly) sql += " AND item_id IS NULL";
        else if (itemId is not null) sql += " AND item_id = $item";
        if (subjectId is not null) sql += " AND subject_id = $subject";
        using var cmd = Command(sql, ("$item", (object?)itemId ?? DBNull.Value), ("$subject", (object?)subjectId ?? DBNull.Value));
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var grantItemId = r.IsDBNull(4) ? null : r.GetString(4);
            var path = grantItemId is null ? null : FindItemById(grantItemId)?.Path;
            grants.Add(new GrantInfo(r.GetString(0), r.GetString(1), Enum.Parse<SubjectType>(r.GetString(2)), r.GetString(3), path, grantItemId));
        }
        return grants;
    }

    public void Dispose() => _db.Dispose();

    private CommandResult? ValidateFolder(CreateFolder c)
    {
        if (string.IsNullOrWhiteSpace(c.ItemId) || c.ItemId == RootId)
            return CommandResult.Fail(ErrorCode.BadRequest, "Invalid item id.");
        if (string.IsNullOrWhiteSpace(c.ParentId))
            return CommandResult.Fail(ErrorCode.BadRequest, "Invalid parent id.");
        if (!NamespacePath.IsValidSegment(c.Name))
            return CommandResult.Fail(ErrorCode.BadRequest, "Invalid folder name.");
        if (c.ParentId != RootId)
        {
            var kind = Scalar("SELECT kind FROM items WHERE id = $v", ("$v", c.ParentId)) as string;
            if (kind is null) return CommandResult.Fail(ErrorCode.NotFound, $"Parent '{c.ParentId}' not found.");
            if (kind != nameof(ItemKind.Folder)) return CommandResult.Fail(ErrorCode.BadRequest, "Parent is not a folder.");
        }
        if (Scalar("SELECT 1 FROM items WHERE (parent_id = $p AND name = $n) OR id = $i", ("$p", c.ParentId), ("$n", c.Name), ("$i", c.ItemId)) is not null)
            return CommandResult.Fail(ErrorCode.AlreadyExists, $"'{c.Name}' already exists.");
        return null;
    }

    private CommandResult? ValidateRename(RenameItem c)
    {
        if (string.IsNullOrWhiteSpace(c.ItemId) || c.ItemId == RootId)
            return CommandResult.Fail(ErrorCode.BadRequest, "Cannot rename root.");
        if (!NamespacePath.IsValidSegment(c.NewName))
            return CommandResult.Fail(ErrorCode.BadRequest, "Invalid item name.");
        var parentId = Scalar("SELECT parent_id FROM items WHERE id = $v", ("$v", c.ItemId)) as string;
        if (parentId is null)
            return CommandResult.Fail(ErrorCode.NotFound, $"Item '{c.ItemId}' not found.");
        if (HasSiblingWithName(parentId, c.NewName, c.ItemId))
            return CommandResult.Fail(ErrorCode.AlreadyExists, $"'{c.NewName}' already exists.");
        return null;
    }

    private CommandResult? ValidateMove(MoveItem c)
    {
        if (string.IsNullOrWhiteSpace(c.ItemId) || c.ItemId == RootId)
            return CommandResult.Fail(ErrorCode.BadRequest, "Cannot move root.");
        if (string.IsNullOrWhiteSpace(c.NewParentId))
            return CommandResult.Fail(ErrorCode.BadRequest, "Invalid destination folder.");

        var name = Scalar("SELECT name FROM items WHERE id = $v", ("$v", c.ItemId)) as string;
        if (name is null)
            return CommandResult.Fail(ErrorCode.NotFound, $"Item '{c.ItemId}' not found.");

        if (c.NewParentId != RootId)
        {
            var destKind = Scalar("SELECT kind FROM items WHERE id = $v", ("$v", c.NewParentId)) as string;
            if (destKind is null)
                return CommandResult.Fail(ErrorCode.NotFound, $"Destination '{c.NewParentId}' not found.");
            if (destKind != nameof(ItemKind.Folder))
                return CommandResult.Fail(ErrorCode.BadRequest, "Destination is not a folder.");
        }

        if (IsAncestorOrSelf(c.ItemId, c.NewParentId))
            return CommandResult.Fail(ErrorCode.BadRequest, "Cannot move a folder into itself or its descendant.");

        if (HasSiblingWithName(c.NewParentId, name, c.ItemId))
            return CommandResult.Fail(ErrorCode.AlreadyExists, $"'{name}' already exists in destination.");
        return null;
    }

    private CommandResult? ValidateDelete(DeleteItem c)
    {
        if (string.IsNullOrWhiteSpace(c.ItemId) || c.ItemId == RootId)
            return CommandResult.Fail(ErrorCode.BadRequest, "Cannot delete root.");
        if (Scalar("SELECT 1 FROM items WHERE id = $v", ("$v", c.ItemId)) is null)
            return CommandResult.Fail(ErrorCode.NotFound, $"Item '{c.ItemId}' not found.");
        return null;
    }

    private bool HasSiblingWithName(string parentId, string name, string excludeItemId) =>
        Scalar(
            "SELECT 1 FROM items WHERE parent_id = $p AND name = $n AND id <> $id",
            ("$p", parentId),
            ("$n", name),
            ("$id", excludeItemId)) is not null;

    private bool IsAncestorOrSelf(string candidateAncestorId, string nodeId)
    {
        if (candidateAncestorId == nodeId)
        {
            return true;
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = nodeId;
        while (current != RootId && visited.Add(current))
        {
            var parentId = Scalar("SELECT parent_id FROM items WHERE id = $v", ("$v", current)) as string;
            if (parentId is null)
            {
                return false;
            }
            if (parentId == candidateAncestorId)
            {
                return true;
            }
            current = parentId;
        }

        return false;
    }

    private void DeleteSubtree(string itemId)
    {
        DeleteFromSubtreeTable("DELETE FROM grants WHERE item_id IN (SELECT id FROM subtree);", itemId);
        DeleteFromSubtreeTable("DELETE FROM uploads WHERE item_id IN (SELECT id FROM subtree);", itemId);
        DeleteFromSubtreeTable("DELETE FROM items WHERE id IN (SELECT id FROM subtree);", itemId);
    }

    private void DeleteFromSubtreeTable(string sqlDeleteStatement, string itemId) =>
        Exec(
            $"""
            WITH RECURSIVE subtree(id) AS (
                SELECT id FROM items WHERE id = $id
                UNION ALL
                SELECT i.id FROM items i JOIN subtree s ON i.parent_id = s.id
            )
            {sqlDeleteStatement}
            """,
            ("$id", itemId));

    private CommandResult? ValidateSession(CreateSession c)
    {
        if (string.IsNullOrWhiteSpace(c.SessionToken) || c.ExpiresAt <= c.At)
            return CommandResult.Fail(ErrorCode.BadRequest, "Invalid session token or expiry.");
        if (Scalar("SELECT 1 FROM sessions WHERE token = $token", ("$token", c.SessionToken)) is not null)
            return CommandResult.Fail(ErrorCode.AlreadyExists, "Session already exists.");
        var disabled = Scalar("SELECT disabled FROM users WHERE id = $id", ("$id", c.UserId));
        return disabled is null ? CommandResult.Fail(ErrorCode.NotFound, "User not found.")
            : (long)disabled != 0 ? CommandResult.Fail(ErrorCode.Unauthorized, "User is disabled.") : null;
    }

    private CommandResult? ValidateRole(CreateRole c)
    {
        if (string.IsNullOrWhiteSpace(c.RoleId) || string.IsNullOrWhiteSpace(c.Name) || c.Permissions is null)
            return CommandResult.Fail(ErrorCode.BadRequest, "Invalid role.");
        if (Scalar("SELECT 1 FROM roles WHERE id = $id OR name = $name", ("$id", c.RoleId), ("$name", c.Name)) is not null)
            return CommandResult.Fail(ErrorCode.AlreadyExists, "Role already exists.");
        string[] allowed = c.Scope switch
        {
            RoleScope.System => [Permission.UserManage, Permission.GroupManage, Permission.RoleManage,
                Permission.ClusterView, Permission.ClusterManage, Permission.AuditView],
            RoleScope.Item => [Permission.ItemList, Permission.FileRead, Permission.FileWrite,
                Permission.ItemCreate, Permission.ItemRename, Permission.ItemDelete,
                Permission.ItemShare, Permission.VersionManage],
            _ => [],
        };
        if (allowed.Length == 0 || c.Permissions.Distinct(StringComparer.Ordinal).Count() != c.Permissions.Count ||
            c.Permissions.Any(p => !allowed.Contains(p, StringComparer.Ordinal)))
            return CommandResult.Fail(ErrorCode.BadRequest, "Role has duplicate or invalid permissions.");
        return null;
    }

    private CommandResult? ValidateGrant(SetGrant c)
    {
        if (Scalar("SELECT 1 FROM grants WHERE id = $id", ("$id", c.GrantId)) is not null)
            return CommandResult.Fail(ErrorCode.AlreadyExists, "Grant already exists.");
        if (Scalar("SELECT 1 FROM roles WHERE id = $id", ("$id", c.RoleId)) is null)
            return CommandResult.Fail(ErrorCode.NotFound, "Role not found.");
        var subjectTable = c.SubjectType switch { SubjectType.User => "users", SubjectType.Group => "groups", _ => null };
        if (subjectTable is null || Scalar($"SELECT 1 FROM {subjectTable} WHERE id = $id", ("$id", c.SubjectId)) is null)
            return CommandResult.Fail(ErrorCode.NotFound, "Grant subject not found.");
        var roleScope = Scalar("SELECT scope FROM roles WHERE id = $id", ("$id", c.RoleId)) as string;
        if ((c.ItemId is null && roleScope != nameof(RoleScope.System)) ||
            (c.ItemId is not null && roleScope != nameof(RoleScope.Item)))
            return CommandResult.Fail(ErrorCode.BadRequest, "Grant scope does not match role scope.");
        if (c.ItemId is not null && Scalar("SELECT 1 FROM items WHERE id = $id", ("$id", c.ItemId)) is null)
            return CommandResult.Fail(ErrorCode.NotFound, "Grant item not found.");
        return null;
    }

    private List<ItemInfo> ReadItems(string sql, params (string, object)[] args)
    {
        var rows = new List<(string Id, string Parent, string Name, string Kind, string Owner, int Ver, long Size, string At)>();
        using (var cmd = Command(sql, args))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read()) rows.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetInt32(5), r.GetInt64(6), r.GetString(7)));
        }
        return rows.Select(x => ToItem(x.Id, x.Parent, x.Name, x.Kind, x.Owner, x.Ver, x.Size, x.At)).ToList();
    }

    private ItemInfo ToItem(string id, string parentId, string name, string kind, string owner, int version, long size, string modifiedAt)
    {
        var parts = new Stack<string>();
        parts.Push(name);
        var p = parentId;
        while (p != RootId)
        {
            using var cmd = Command("SELECT parent_id, name FROM items WHERE id = $v", ("$v", p));
            using var r = cmd.ExecuteReader();
            if (!r.Read()) break;
            p = r.GetString(0);
            parts.Push(r.GetString(1));
        }
        return new ItemInfo(id, "/" + string.Join('/', parts), name, Enum.Parse<ItemKind>(kind), size, owner, version, Parse(modifiedAt));
    }

    private UserCredentials? ReadUser(string column, string value)
    {
        using var cmd = Command($"SELECT id, username, password_hash, salt, disabled FROM users WHERE {column} = $v", ("$v", value));
        using var r = cmd.ExecuteReader();
        return r.Read() ? new UserCredentials(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt64(4) != 0) : null;
    }

    private static string Format(DateTime t) => t.ToUniversalTime().ToString(TimeFormat, CultureInfo.InvariantCulture);

    private static DateTime Parse(string s) =>
        DateTime.ParseExact(s, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    private SqliteCommand Command(string sql, params (string Name, object Value)[] args)
    {
        var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        return cmd;
    }

    private object? Scalar(string sql, params (string, object)[] args)
    {
        using var cmd = Command(sql, args);
        return cmd.ExecuteScalar();
    }

    private void Exec(string sql, params (string, object)[] args)
    {
        using var cmd = Command(sql, args);
        cmd.ExecuteNonQuery();
    }
}
