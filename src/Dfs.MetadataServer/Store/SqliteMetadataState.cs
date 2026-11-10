using System.Globalization;
using Dfs.Shared.Commands;
using Dfs.Shared.Messages;
using Dfs.Shared.Metadata;
using CreateFolder = Dfs.Shared.Commands.CreateFolder;
using CreateUser = Dfs.Shared.Commands.CreateUser;
using Microsoft.Data.Sqlite;

namespace Dfs.MetadataServer.Store;

/// <summary>SQLite state derived from the op log: minimal <c>users</c> and <c>items</c> tables plus <c>applied_seq</c>.</summary>
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
        CreateUser c => Scalar("SELECT 1 FROM users WHERE username = $v OR id = $i", ("$v", c.Username), ("$i", c.UserId)) is not null
            ? CommandResult.Fail(ErrorCode.AlreadyExists, $"User '{c.Username}' already exists.")
            : null,
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
            case CreateUser c:
                Exec(
                    "INSERT INTO users (id, username, password_hash, salt, disabled, created_at) VALUES ($id, $u, $h, $s, 0, $t)",
                    ("$id", c.UserId), ("$u", c.Username), ("$h", c.PasswordHash), ("$s", c.Salt), ("$t", Format(c.At)));
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

    public void Dispose() => _db.Dispose();

    private CommandResult? ValidateFolder(CreateFolder c)
    {
        if (string.IsNullOrWhiteSpace(c.Name) || c.Name.Contains('/'))
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
