using System.Globalization;
using System.Text.Json;
using Dfs.MetadataServer.Handlers;
using Dfs.Shared.Commands;
using Dfs.Shared.Framing;
using Dfs.Shared.Messages;
using Dfs.Shared.Metadata;
using Dfs.Shared.Security;
using CreateFolder = Dfs.Shared.Commands.CreateFolder;
using CreateUser = Dfs.Shared.Commands.CreateUser;
using DeleteItem = Dfs.Shared.Commands.DeleteItem;
using MoveItem = Dfs.Shared.Commands.MoveItem;
using RenameItem = Dfs.Shared.Commands.RenameItem;
using CreateRole = Dfs.Shared.Commands.CreateRole;
using CreateGroup = Dfs.Shared.Commands.CreateGroup;
using SetGroupMember = Dfs.Shared.Commands.SetGroupMember;
using UpdateRole = Dfs.Shared.Commands.UpdateRole;
using RevokeGrant = Dfs.Shared.Commands.RevokeGrant;
using SetUserDisabled = Dfs.Shared.Commands.SetUserDisabled;
using CommandBeginUpload = Dfs.Shared.Commands.BeginUpload;
using CommandCommitUpload = Dfs.Shared.Commands.CommitUpload;
using CommandAbortUpload = Dfs.Shared.Commands.AbortUpload;
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
        CreateGroup c => ValidateGroup(c),
        SetGroupMember c => ValidateGroupMember(c),
        CreateRole c => ValidateRole(c),
        UpdateRole c => ValidateRoleUpdate(c),
        SetGrant c => ValidateGrant(c),
        RevokeGrant c => string.IsNullOrWhiteSpace(c.GrantId)
            ? CommandResult.Fail(ErrorCode.BadRequest, "Invalid grant id.")
            : Scalar("SELECT 1 FROM grants WHERE id = $id", ("$id", c.GrantId)) is null
                ? CommandResult.Fail(ErrorCode.NotFound, "Grant not found.") : null,
        CreateSession c => ValidateSession(c),
        DeleteSession => null,
        RegisterDataServer c => ValidateRegisterDataServer(c),
        UpdateHeartbeat c => ValidateUpdateHeartbeat(c),
        CommandBeginUpload c => ValidateBeginUpload(c),
        CommandCommitUpload c => ValidateCommitUpload(c),
        CommandAbortUpload c => ValidateAbortUpload(c),
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
                // The Owner grant is derived from this command in the same transaction.
                Exec("INSERT INTO grants (id, role_id, subject_type, subject_id, item_id) " +
                     "SELECT $grant, $role, 'User', $owner, $item " +
                     "WHERE EXISTS (SELECT 1 FROM roles WHERE id = $role) " +
                     "AND EXISTS (SELECT 1 FROM users WHERE id = $owner)",
                    ("$grant", "g-owner-" + c.ItemId), ("$role", BuiltInRoles.OwnerId),
                    ("$owner", c.OwnerId), ("$item", c.ItemId));
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
            case CreateGroup c:
                Exec("INSERT INTO groups (id, name) VALUES ($id, $name)", ("$id", c.GroupId), ("$name", c.Name));
                break;
            case SetGroupMember c:
                if (c.Member)
                    Exec("INSERT OR IGNORE INTO group_members (group_id, user_id) VALUES ($group, $user)",
                        ("$group", c.GroupId), ("$user", c.UserId));
                else
                    Exec("DELETE FROM group_members WHERE group_id = $group AND user_id = $user",
                        ("$group", c.GroupId), ("$user", c.UserId));
                break;
            case CreateRole c:
                Exec("INSERT INTO roles (id, name, scope) VALUES ($id, $name, $scope)",
                    ("$id", c.RoleId), ("$name", c.Name), ("$scope", c.Scope.ToString()));
                foreach (var permission in c.Permissions)
                    Exec("INSERT INTO role_permissions (role_id, permission) VALUES ($id, $permission)",
                        ("$id", c.RoleId), ("$permission", permission));
                break;
            case UpdateRole c:
                Exec("DELETE FROM role_permissions WHERE role_id = $id", ("$id", c.RoleId));
                foreach (var permission in c.Permissions)
                    Exec("INSERT INTO role_permissions (role_id, permission) VALUES ($id, $permission)",
                        ("$id", c.RoleId), ("$permission", permission));
                break;
            case SetGrant c:
                Exec("INSERT INTO grants (id, role_id, subject_type, subject_id, item_id) VALUES ($id, $role, $type, $subject, $item)",
                    ("$id", c.GrantId), ("$role", c.RoleId), ("$type", c.SubjectType.ToString()),
                    ("$subject", c.SubjectId), ("$item", (object?)c.ItemId ?? DBNull.Value));
                break;
            case RevokeGrant c:
                Exec("DELETE FROM grants WHERE id = $id", ("$id", c.GrantId));
                break;
            case CreateSession c:
                Exec("INSERT INTO sessions (token, user_id, expires_at) VALUES ($token, $user, $expiry)",
                    ("$token", c.SessionToken), ("$user", c.UserId), ("$expiry", Format(c.ExpiresAt)));
                break;
            case DeleteSession c:
                Exec("DELETE FROM sessions WHERE token = $token", ("$token", c.SessionToken));
                break;
            case RegisterDataServer c:
                Exec(
                    """
                    INSERT INTO data_servers (id, address, status, capacity_bytes, free_bytes, chunk_count, last_heartbeat)
                    VALUES ($id, $addr, 'Alive', $cap, $free, 0, $t)
                    ON CONFLICT(id) DO UPDATE SET
                        address = $addr,
                        status = 'Alive',
                        capacity_bytes = $cap,
                        free_bytes = $free,
                        last_heartbeat = $t
                    """,
                    ("$id", c.NodeId), ("$addr", c.Address), ("$cap", c.CapacityBytes),
                    ("$free", c.FreeBytes), ("$t", Format(c.At)));
                break;
            case UpdateHeartbeat c:
                Exec(
                    """
                    UPDATE data_servers
                    SET free_bytes = $free, chunk_count = $chunks, status = 'Alive', last_heartbeat = $t
                    WHERE id = $id
                    """,
                    ("$id", c.NodeId), ("$free", c.FreeBytes), ("$chunks", c.ChunkCount), ("$t", Format(c.At)));
                break;
            case CommandBeginUpload c:
                var chunksJson = JsonSerializer.Serialize(c.Chunks.ToList(), FrameSerialization.JsonOptions);
                Exec(
                    """
                    INSERT INTO uploads (id, item_id, parent_id, name, base_version, size, chunk_size, created_by, created_at, chunks_json)
                    VALUES ($id, $item, $parent, $name, $base, $size, $chunkSize, $by, $at, $json)
                    """,
                    ("$id", c.UploadId), ("$item", c.ItemId), ("$parent", c.ParentId), ("$name", c.Name),
                    ("$base", c.BaseVersion), ("$size", c.Size), ("$chunkSize", c.ChunkSize),
                    ("$by", c.CreatedBy), ("$at", Format(c.At)), ("$json", chunksJson));
                break;
            case CommandCommitUpload c:
                var upload = FindUpload(c.UploadId);
                if (upload is null) throw new InvalidOperationException($"Upload '{c.UploadId}' not found during apply.");
                var newVersion = upload.BaseVersion + 1;
                var itemExists = Scalar("SELECT 1 FROM items WHERE id = $id", ("$id", upload.ItemId)) is not null;
                if (itemExists)
                {
                    Exec(
                        "UPDATE items SET current_version = $v, size = $s, modified_at = $t WHERE id = $id",
                        ("$v", newVersion), ("$s", upload.Size), ("$t", Format(c.At)), ("$id", upload.ItemId));
                }
                else
                {
                    Exec(
                        "INSERT INTO items (id, parent_id, name, kind, owner_id, current_version, size, modified_at) VALUES ($id, $p, $n, 'File', $o, $v, $s, $t)",
                        ("$id", upload.ItemId), ("$p", upload.ParentId), ("$n", upload.Name), ("$o", upload.CreatedBy),
                        ("$v", newVersion), ("$s", upload.Size), ("$t", Format(c.At)));
                    // The Owner grant is derived from this command in the same transaction.
                    Exec("INSERT INTO grants (id, role_id, subject_type, subject_id, item_id) " +
                         "SELECT $grant, $role, 'User', $owner, $item " +
                         "WHERE EXISTS (SELECT 1 FROM roles WHERE id = $role) " +
                         "AND EXISTS (SELECT 1 FROM users WHERE id = $owner)",
                        ("$grant", "g-owner-" + upload.ItemId), ("$role", BuiltInRoles.OwnerId),
                        ("$owner", upload.CreatedBy), ("$item", upload.ItemId));
                }

                Exec(
                    "INSERT INTO file_versions (item_id, version, size, created_by, created_at) VALUES ($id, $v, $s, $by, $at)",
                    ("$id", upload.ItemId), ("$v", newVersion), ("$s", upload.Size), ("$by", upload.CreatedBy), ("$at", Format(c.At)));

                var checksumsByChunkId = c.Checksums.ToDictionary(cs => cs.ChunkId);
                for (var i = 0; i < upload.Chunks.Count; i++)
                {
                    var planned = upload.Chunks[i];
                    var cs = checksumsByChunkId[planned.ChunkId];
                    Exec(
                        "INSERT INTO chunks (id, size, sha256) VALUES ($id, $s, $sha) ON CONFLICT(id) DO UPDATE SET size = $s, sha256 = $sha",
                        ("$id", cs.ChunkId), ("$s", cs.Size), ("$sha", cs.Sha256));
                    Exec(
                        "INSERT INTO version_chunks (item_id, version, idx, chunk_id) VALUES ($item, $v, $idx, $chunk)",
                        ("$item", upload.ItemId), ("$v", newVersion), ("$idx", planned.Index), ("$chunk", cs.ChunkId));

                    foreach (var serverAddr in planned.Servers)
                    {
                        var serverId = Scalar("SELECT id FROM data_servers WHERE address = $addr OR id = $addr", ("$addr", serverAddr)) as string;
                        if (serverId is not null)
                        {
                            Exec("INSERT OR IGNORE INTO chunk_locations (chunk_id, server_id) VALUES ($c, $s)", ("$c", cs.ChunkId), ("$s", serverId));
                            Exec("UPDATE data_servers SET chunk_count = chunk_count + 1, free_bytes = MAX(0, free_bytes - $size) WHERE id = $s", ("$size", cs.Size), ("$s", serverId));
                        }
                    }
                }

                if (c.KeepVersions > 0)
                {
                    Exec("DELETE FROM file_versions WHERE item_id = $id AND version <= ($v - $keep)", ("$id", upload.ItemId), ("$v", newVersion), ("$keep", c.KeepVersions));
                    Exec("DELETE FROM version_chunks WHERE item_id = $id AND version <= ($v - $keep)", ("$id", upload.ItemId), ("$v", newVersion), ("$keep", c.KeepVersions));
                }

                Exec("DELETE FROM uploads WHERE id = $id", ("$id", c.UploadId));
                break;
            case CommandAbortUpload c:
                Exec("DELETE FROM uploads WHERE id = $id", ("$id", c.UploadId));
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

    public UploadRecord? FindUpload(string uploadId)
    {
        using var cmd = Command("SELECT id, item_id, parent_id, name, created_by, size, base_version, chunk_size, chunks_json FROM uploads WHERE id = $id", ("$id", uploadId));
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;

        var id = r.GetString(0);
        var itemId = r.GetString(1);
        var parentId = r.GetString(2);
        var name = r.GetString(3);
        var createdBy = r.GetString(4);
        var size = r.GetInt64(5);
        var baseVersion = r.GetInt32(6);
        var chunkSize = r.GetInt32(7);
        var chunksJson = r.GetString(8);
        var chunks = JsonSerializer.Deserialize<List<PlannedChunk>>(chunksJson, FrameSerialization.JsonOptions) ?? new List<PlannedChunk>();

        return new UploadRecord(id, itemId, parentId, name, createdBy, size, baseVersion, chunkSize, new ValueList<PlannedChunk>(chunks));
    }

    public IReadOnlyList<ChunkRecord> GetVersionChunks(string itemId, int version)
    {
        using var cmd = Command("""
            SELECT vc.idx, c.id, c.size, c.sha256, ds.address
            FROM version_chunks vc
            JOIN chunks c ON vc.chunk_id = c.id
            LEFT JOIN chunk_locations cl ON c.id = cl.chunk_id
            LEFT JOIN data_servers ds ON cl.server_id = ds.id
            WHERE vc.item_id = $item AND vc.version = $v
            ORDER BY vc.idx, ds.address
            """, ("$item", itemId), ("$v", version));

        using var r = cmd.ExecuteReader();
        var map = new Dictionary<int, (string ChunkId, long Size, string Sha256, List<string> Servers)>();
        while (r.Read())
        {
            var idx = r.GetInt32(0);
            var chunkId = r.GetString(1);
            var size = r.GetInt64(2);
            var sha256 = r.GetString(3);
            var addr = r.IsDBNull(4) ? null : r.GetString(4);

            if (!map.TryGetValue(idx, out var entry))
            {
                entry = (chunkId, size, sha256, new List<string>());
                map[idx] = entry;
            }
            if (!string.IsNullOrEmpty(addr) && !entry.Servers.Contains(addr))
            {
                entry.Servers.Add(addr);
            }
        }

        var list = new List<ChunkRecord>(map.Count);
        foreach (var (idx, entry) in map.OrderBy(kv => kv.Key))
        {
            list.Add(new ChunkRecord(idx, entry.ChunkId, entry.Size, entry.Sha256, new ValueList<string>(entry.Servers)));
        }
        return list;
    }

    public IReadOnlyList<VersionInfo> ListVersions(string itemId)
    {
        using var cmd = Command("""
            SELECT fv.version, fv.size, fv.created_by, fv.created_at, COUNT(vc.chunk_id)
            FROM file_versions fv
            LEFT JOIN version_chunks vc ON fv.item_id = vc.item_id AND fv.version = vc.version
            WHERE fv.item_id = $item
            GROUP BY fv.item_id, fv.version
            ORDER BY fv.version DESC
            """, ("$item", itemId));

        using var r = cmd.ExecuteReader();
        var list = new List<VersionInfo>();
        while (r.Read())
        {
            var version = r.GetInt32(0);
            var size = r.GetInt64(1);
            var createdBy = r.GetString(2);
            var createdAt = Parse(r.GetString(3));
            var chunkCount = r.GetInt32(4);
            list.Add(new VersionInfo(version, size, createdBy, createdAt, chunkCount));
        }
        return list;
    }

    public UserCredentials? FindUserByName(string username) => ReadUser("username", username);

    public UserCredentials? FindUserById(string userId) => ReadUser("id", userId);

    public IReadOnlyList<UserInfo> ListUsers()
    {
        var list = new List<UserInfo>();
        using var cmd = Command("SELECT id, username, disabled FROM users ORDER BY username");
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var id = r.GetString(0);
            var groups = new ValueList<string>();
            using var membership = Command("SELECT group_id FROM group_members WHERE user_id = $id ORDER BY group_id", ("$id", id));
            using var memberReader = membership.ExecuteReader();
            while (memberReader.Read()) groups.Add(memberReader.GetString(0));
            list.Add(new UserInfo(id, r.GetString(1), r.GetInt64(2) != 0, groups));
        }
        return list;
    }

    public IReadOnlyList<GroupInfo> ListGroups()
    {
        var groups = new List<GroupInfo>();
        using var cmd = Command("SELECT id, name FROM groups ORDER BY name");
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var id = r.GetString(0);
            var members = new ValueList<string>();
            using var membership = Command("SELECT user_id FROM group_members WHERE group_id = $id ORDER BY user_id", ("$id", id));
            using var memberReader = membership.ExecuteReader();
            while (memberReader.Read()) members.Add(memberReader.GetString(0));
            groups.Add(new GroupInfo(id, r.GetString(1), members));
        }
        return groups;
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

    public int MarkDeadServers(DateTime cutoff)
    {
        var utcCutoff = cutoff.ToUniversalTime();
        var deadIds = new List<string>();
        using (var selectCmd = Command("SELECT id, last_heartbeat FROM data_servers WHERE status = 'Alive'"))
        using (var reader = selectCmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var id = reader.GetString(0);
                var lastHeartbeat = Parse(reader.GetString(1));
                if (lastHeartbeat < utcCutoff)
                {
                    deadIds.Add(id);
                }
            }
        }

        if (deadIds.Count == 0) return 0;

        using var tx = _db.BeginTransaction();
        using var updateCmd = _db.CreateCommand();
        updateCmd.Transaction = tx;
        updateCmd.CommandText = "UPDATE data_servers SET status = 'Dead' WHERE id = $id";
        var idParam = updateCmd.Parameters.Add("$id", SqliteType.Text);

        var count = 0;
        foreach (var id in deadIds)
        {
            idParam.Value = id;
            count += updateCmd.ExecuteNonQuery();
        }

        tx.Commit();
        return count;
    }

    public IReadOnlyList<DataServerInfo> ListDataServers()
    {
        using var cmd = Command("SELECT id, address, status, capacity_bytes, free_bytes, chunk_count, last_heartbeat FROM data_servers ORDER BY id");
        using var reader = cmd.ExecuteReader();
        var list = new List<DataServerInfo>();
        while (reader.Read())
        {
            var id = reader.GetString(0);
            var addr = reader.GetString(1);
            var statusStr = reader.GetString(2);
            var status = Enum.TryParse<DataServerStatus>(statusStr, true, out var parsedStatus)
                ? parsedStatus
                : DataServerStatus.Dead;
            var capacity = reader.GetInt64(3);
            var free = reader.GetInt64(4);
            var chunks = reader.GetInt32(5);
            var lastHeartbeat = DateTime.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
            list.Add(new DataServerInfo(id, addr, status, capacity, free, chunks, lastHeartbeat));
        }
        return list;
    }

    public DataServerInfo? FindDataServerById(string nodeId)
    {
        using var cmd = Command("SELECT id, address, status, capacity_bytes, free_bytes, chunk_count, last_heartbeat FROM data_servers WHERE id = $id", ("$id", nodeId));
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        var id = reader.GetString(0);
        var addr = reader.GetString(1);
        var statusStr = reader.GetString(2);
        var status = Enum.TryParse<DataServerStatus>(statusStr, true, out var parsedStatus)
            ? parsedStatus
            : DataServerStatus.Dead;
        var capacity = reader.GetInt64(3);
        var free = reader.GetInt64(4);
        var chunks = reader.GetInt32(5);
        var lastHeartbeat = DateTime.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        return new DataServerInfo(id, addr, status, capacity, free, chunks, lastHeartbeat);
    }

    public void Dispose() => _db.Dispose();

    private CommandResult? ValidateFolder(CreateFolder c)
    {
        if (string.IsNullOrWhiteSpace(c.ItemId) || c.ItemId == RootId)
            return CommandResult.Fail(ErrorCode.BadRequest, "Invalid item id.");
        if (string.IsNullOrWhiteSpace(c.ParentId))
            return CommandResult.Fail(ErrorCode.BadRequest, "Invalid parent id.");
        if (string.IsNullOrWhiteSpace(c.OwnerId))
            return CommandResult.Fail(ErrorCode.BadRequest, "Invalid owner id.");
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
        if (Scalar("SELECT 1 FROM roles WHERE id = $id", ("$id", BuiltInRoles.OwnerId)) is not null &&
            Scalar("SELECT 1 FROM users WHERE id = $id", ("$id", c.OwnerId)) is not null &&
            Scalar("SELECT 1 FROM grants WHERE id = $id", ("$id", "g-owner-" + c.ItemId)) is not null)
            return CommandResult.Fail(ErrorCode.AlreadyExists, "Owner grant id already exists.");
        return null;
    }

    private CommandResult? ValidateGroup(CreateGroup c)
    {
        if (string.IsNullOrWhiteSpace(c.GroupId) || string.IsNullOrWhiteSpace(c.Name))
            return CommandResult.Fail(ErrorCode.BadRequest, "Invalid group.");
        return Scalar("SELECT 1 FROM groups WHERE id = $id OR name = $name", ("$id", c.GroupId), ("$name", c.Name)) is not null
            ? CommandResult.Fail(ErrorCode.AlreadyExists, "Group already exists.") : null;
    }

    private CommandResult? ValidateGroupMember(SetGroupMember c)
    {
        if (string.IsNullOrWhiteSpace(c.GroupId) || string.IsNullOrWhiteSpace(c.UserId))
            return CommandResult.Fail(ErrorCode.BadRequest, "Invalid group or user id.");
        if (Scalar("SELECT 1 FROM groups WHERE id = $id", ("$id", c.GroupId)) is null)
            return CommandResult.Fail(ErrorCode.NotFound, "Group not found.");
        return Scalar("SELECT 1 FROM users WHERE id = $id", ("$id", c.UserId)) is null
            ? CommandResult.Fail(ErrorCode.NotFound, "User not found.") : null;
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

    private static CommandResult? ValidateRegisterDataServer(RegisterDataServer c)
    {
        if (string.IsNullOrWhiteSpace(c.NodeId))
            return CommandResult.Fail(ErrorCode.BadRequest, "NodeId must not be empty.");
        if (string.IsNullOrWhiteSpace(c.Address))
            return CommandResult.Fail(ErrorCode.BadRequest, "Address must not be empty.");
        if (c.CapacityBytes < 0)
            return CommandResult.Fail(ErrorCode.BadRequest, "CapacityBytes cannot be negative.");
        if (c.FreeBytes < 0)
            return CommandResult.Fail(ErrorCode.BadRequest, "FreeBytes cannot be negative.");
        return null;
    }

    private CommandResult? ValidateUpdateHeartbeat(UpdateHeartbeat c)
    {
        if (string.IsNullOrWhiteSpace(c.NodeId))
            return CommandResult.Fail(ErrorCode.BadRequest, "NodeId must not be empty.");
        if (c.FreeBytes < 0)
            return CommandResult.Fail(ErrorCode.BadRequest, "FreeBytes cannot be negative.");
        if (c.ChunkCount < 0)
            return CommandResult.Fail(ErrorCode.BadRequest, "ChunkCount cannot be negative.");
        if (Scalar("SELECT 1 FROM data_servers WHERE id = $id", ("$id", c.NodeId)) is null)
            return CommandResult.Fail(ErrorCode.NotFound, $"Data server '{c.NodeId}' not registered.");
        return null;
    }

    private CommandResult? ValidateRole(CreateRole c)
    {
        if (string.IsNullOrWhiteSpace(c.RoleId) || string.IsNullOrWhiteSpace(c.Name) || c.Permissions is null)
            return CommandResult.Fail(ErrorCode.BadRequest, "Invalid role.");
        if (Scalar("SELECT 1 FROM roles WHERE id = $id OR name = $name", ("$id", c.RoleId), ("$name", c.Name)) is not null)
            return CommandResult.Fail(ErrorCode.AlreadyExists, "Role already exists.");
        return ValidatePermissions(c.Scope, c.Permissions);
    }

    private static CommandResult? ValidatePermissions(RoleScope scope, ValueList<string>? permissions)
    {
        if (permissions is null)
            return CommandResult.Fail(ErrorCode.BadRequest, "Role permissions are required.");
        string[] allowed = scope switch
        {
            RoleScope.System => [Permission.UserManage, Permission.GroupManage, Permission.RoleManage,
                Permission.ClusterView, Permission.ClusterManage, Permission.AuditView],
            RoleScope.Item => [Permission.ItemList, Permission.FileRead, Permission.FileWrite,
                Permission.ItemCreate, Permission.ItemRename, Permission.ItemDelete,
                Permission.ItemShare, Permission.VersionManage],
            _ => [],
        };
        if (allowed.Length == 0 || permissions.Distinct(StringComparer.Ordinal).Count() != permissions.Count ||
            permissions.Any(p => !allowed.Contains(p, StringComparer.Ordinal)))
            return CommandResult.Fail(ErrorCode.BadRequest, "Role has duplicate or invalid permissions.");
        return null;
    }

    private CommandResult? ValidateRoleUpdate(UpdateRole c)
    {
        if (string.IsNullOrWhiteSpace(c.RoleId))
            return CommandResult.Fail(ErrorCode.BadRequest, "Invalid role id.");
        using var cmd = Command("SELECT scope FROM roles WHERE id = $id", ("$id", c.RoleId));
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return CommandResult.Fail(ErrorCode.NotFound, "Role not found.");
        if (c.RoleId is BuiltInRoles.AdminId or BuiltInRoles.AuditorId or BuiltInRoles.OwnerId or BuiltInRoles.EditorId or BuiltInRoles.ViewerId)
            return CommandResult.Fail(ErrorCode.BadRequest, "Built-in roles cannot be changed.");
        return ValidatePermissions(Enum.Parse<RoleScope>(r.GetString(0)), c.Permissions);
    }

    private CommandResult? ValidateGrant(SetGrant c)
    {
        if (string.IsNullOrWhiteSpace(c.GrantId) || string.IsNullOrWhiteSpace(c.RoleId) ||
            string.IsNullOrWhiteSpace(c.SubjectId) || (c.ItemId is not null && string.IsNullOrWhiteSpace(c.ItemId)))
            return CommandResult.Fail(ErrorCode.BadRequest, "Invalid grant id, role id, subject id, or item id.");
        if (Scalar("SELECT 1 FROM grants WHERE id = $id", ("$id", c.GrantId)) is not null)
            return CommandResult.Fail(ErrorCode.AlreadyExists, "Grant already exists.");
        if (Scalar("SELECT 1 FROM roles WHERE id = $id", ("$id", c.RoleId)) is null)
            return CommandResult.Fail(ErrorCode.NotFound, "Role not found.");
        var subjectTable = c.SubjectType switch { SubjectType.User => "users", SubjectType.Group => "groups", _ => null };
        if (subjectTable is null)
            return CommandResult.Fail(ErrorCode.BadRequest, "Invalid grant subject type.");
        if (Scalar($"SELECT 1 FROM {subjectTable} WHERE id = $id", ("$id", c.SubjectId)) is null)
            return CommandResult.Fail(ErrorCode.NotFound, "Grant subject not found.");
        var roleScope = Scalar("SELECT scope FROM roles WHERE id = $id", ("$id", c.RoleId)) as string;
        if ((c.ItemId is null && roleScope != nameof(RoleScope.System)) ||
            (c.ItemId is not null && roleScope != nameof(RoleScope.Item)))
            return CommandResult.Fail(ErrorCode.BadRequest, "Grant scope does not match role scope.");
        if (c.ItemId is not null && Scalar("SELECT 1 FROM items WHERE id = $id", ("$id", c.ItemId)) is null)
            return CommandResult.Fail(ErrorCode.NotFound, "Grant item not found.");
        return null;
    }

    private CommandResult? ValidateBeginUpload(CommandBeginUpload c)
    {
        if (string.IsNullOrWhiteSpace(c.UploadId))
            return CommandResult.Fail(ErrorCode.BadRequest, "UploadId must not be empty.");
        if (string.IsNullOrWhiteSpace(c.ItemId) || c.ItemId == RootId)
            return CommandResult.Fail(ErrorCode.BadRequest, "ItemId is invalid.");
        if (string.IsNullOrWhiteSpace(c.ParentId))
            return CommandResult.Fail(ErrorCode.BadRequest, "ParentId is invalid.");
        if (string.IsNullOrWhiteSpace(c.Name) || !NamespacePath.IsValidSegment(c.Name))
            return CommandResult.Fail(ErrorCode.BadRequest, "Invalid file name.");
        if (string.IsNullOrWhiteSpace(c.CreatedBy))
            return CommandResult.Fail(ErrorCode.BadRequest, "CreatedBy must not be empty.");
        if (c.Size < 0)
            return CommandResult.Fail(ErrorCode.BadRequest, "Size cannot be negative.");
        if (c.BaseVersion < 0)
            return CommandResult.Fail(ErrorCode.BadRequest, "BaseVersion cannot be negative.");
        if (c.ChunkSize <= 0)
            return CommandResult.Fail(ErrorCode.BadRequest, "ChunkSize must be positive.");

        if (Scalar("SELECT 1 FROM uploads WHERE id = $id", ("$id", c.UploadId)) is not null)
            return CommandResult.Fail(ErrorCode.AlreadyExists, $"Upload '{c.UploadId}' already exists.");

        if (c.ParentId != RootId)
        {
            var parentKind = Scalar("SELECT kind FROM items WHERE id = $v", ("$v", c.ParentId)) as string;
            if (parentKind is null)
                return CommandResult.Fail(ErrorCode.NotFound, "Parent folder does not exist.");
            if (parentKind != nameof(ItemKind.Folder))
                return CommandResult.Fail(ErrorCode.BadRequest, "Parent is not a folder.");
        }

        var existingItem = ReadItems($"SELECT {ItemColumns} FROM items WHERE id = $id", ("$id", c.ItemId)).FirstOrDefault();
        if (existingItem is not null)
        {
            if (existingItem.Kind != ItemKind.File)
                return CommandResult.Fail(ErrorCode.BadRequest, "Item is not a file.");
            if (existingItem.CurrentVersion != c.BaseVersion)
                return CommandResult.Fail(ErrorCode.Conflict, $"BaseVersion {c.BaseVersion} does not match current version {existingItem.CurrentVersion}.", VersionDetails(existingItem.CurrentVersion));
        }
        else
        {
            if (c.BaseVersion != 0)
                return CommandResult.Fail(ErrorCode.Conflict, "BaseVersion must be 0 for a new file.", VersionDetails(0));

            var sibling = Scalar("SELECT 1 FROM items WHERE parent_id = $p AND name = $n", ("$p", c.ParentId), ("$n", c.Name));
            if (sibling is not null)
                return CommandResult.Fail(ErrorCode.AlreadyExists, $"Item '{c.Name}' already exists in parent.");
        }

        return null;
    }

    private CommandResult? ValidateCommitUpload(CommandCommitUpload c)
    {
        if (string.IsNullOrWhiteSpace(c.UploadId))
            return CommandResult.Fail(ErrorCode.BadRequest, "UploadId must not be empty.");
        if (c.Checksums is null)
            return CommandResult.Fail(ErrorCode.BadRequest, "Checksums must not be null.");

        var upload = FindUpload(c.UploadId);
        if (upload is null)
            return CommandResult.Fail(ErrorCode.NotFound, $"Upload '{c.UploadId}' not found.");

        if (c.Checksums.Count != upload.Chunks.Count)
            return CommandResult.Fail(ErrorCode.BadRequest, $"Expected {upload.Chunks.Count} checksums, got {c.Checksums.Count}.");

        var expectedByChunkId = upload.Chunks.ToDictionary(ch => ch.ChunkId);
        var seenChunkIds = new HashSet<string>(StringComparer.Ordinal);
        long totalSize = 0;

        for (var i = 0; i < c.Checksums.Count; i++)
        {
            var actual = c.Checksums[i];
            if (actual is null)
                return CommandResult.Fail(ErrorCode.BadRequest, $"Checksum at index {i} cannot be null.");
            if (string.IsNullOrWhiteSpace(actual.ChunkId))
                return CommandResult.Fail(ErrorCode.BadRequest, $"ChunkId at index {i} must not be empty.");
            if (!seenChunkIds.Add(actual.ChunkId))
                return CommandResult.Fail(ErrorCode.BadRequest, $"Duplicate checksum for chunk '{actual.ChunkId}'.");
            if (!expectedByChunkId.TryGetValue(actual.ChunkId, out var planned))
                return CommandResult.Fail(ErrorCode.BadRequest, $"Unexpected chunk '{actual.ChunkId}' for upload.");

            var expectedSize = Math.Min((long)upload.ChunkSize, upload.Size - (long)planned.Index * upload.ChunkSize);
            if (actual.Size != expectedSize)
                return CommandResult.Fail(ErrorCode.BadRequest, $"Chunk '{actual.ChunkId}' size mismatch: expected {expectedSize}, got {actual.Size}.");

            if (!IsLowerHex64(actual.Sha256))
                return CommandResult.Fail(ErrorCode.BadRequest, $"Chunk '{actual.ChunkId}' SHA-256 is not a valid 64-character lowercase hex string.");

            totalSize += actual.Size;
        }

        if (totalSize != upload.Size)
            return CommandResult.Fail(ErrorCode.BadRequest, $"Total chunk size ({totalSize}) does not match upload size ({upload.Size}).");

        var existingItem = ReadItems($"SELECT {ItemColumns} FROM items WHERE id = $id", ("$id", upload.ItemId)).FirstOrDefault();
        if (existingItem is not null)
        {
            if (existingItem.CurrentVersion != upload.BaseVersion)
                return CommandResult.Fail(ErrorCode.Conflict, $"File version has changed to {existingItem.CurrentVersion}.", VersionDetails(existingItem.CurrentVersion));
        }
        else
        {
            if (upload.BaseVersion != 0)
                return CommandResult.Fail(ErrorCode.Conflict, "File does not exist.", VersionDetails(0));

            if (upload.ParentId != RootId && Scalar("SELECT 1 FROM items WHERE id = $p", ("$p", upload.ParentId)) is null)
                return CommandResult.Fail(ErrorCode.NotFound, "Parent folder no longer exists.");

            var sibling = Scalar("SELECT 1 FROM items WHERE parent_id = $p AND name = $n", ("$p", upload.ParentId), ("$n", upload.Name));
            if (sibling is not null)
                return CommandResult.Fail(ErrorCode.AlreadyExists, $"Item '{upload.Name}' already exists.");

            if (Scalar("SELECT 1 FROM roles WHERE id = $id", ("$id", BuiltInRoles.OwnerId)) is not null &&
                Scalar("SELECT 1 FROM users WHERE id = $id", ("$id", upload.CreatedBy)) is not null &&
                Scalar("SELECT 1 FROM grants WHERE id = $id", ("$id", "g-owner-" + upload.ItemId)) is not null)
                return CommandResult.Fail(ErrorCode.AlreadyExists, "Owner grant id already exists.");
        }

        return null;
    }

    private static bool IsLowerHex64(string? s)
    {
        if (string.IsNullOrEmpty(s) || s.Length != 64) return false;
        foreach (var ch in s)
        {
            if (!((ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f')))
                return false;
        }
        return true;
    }

    private CommandResult? ValidateAbortUpload(CommandAbortUpload c)
    {
        if (string.IsNullOrWhiteSpace(c.UploadId))
            return CommandResult.Fail(ErrorCode.BadRequest, "UploadId must not be empty.");
        if (Scalar("SELECT 1 FROM uploads WHERE id = $id", ("$id", c.UploadId)) is null)
            return CommandResult.Fail(ErrorCode.NotFound, $"Upload '{c.UploadId}' not found.");
        return null;
    }

    private static Dictionary<string, JsonElement> VersionDetails(int currentVersion)
    {
        using var doc = JsonDocument.Parse($"{{\"currentVersion\":{currentVersion}}}");
        return new Dictionary<string, JsonElement>
        {
            ["currentVersion"] = doc.RootElement.GetProperty("currentVersion").Clone()
        };
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
