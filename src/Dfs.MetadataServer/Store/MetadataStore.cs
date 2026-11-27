using Dfs.MetadataServer.Config;
using Dfs.Shared.Commands;
using Dfs.Shared.Messages;
using Dfs.Shared.Metadata;
using CreateFolder = Dfs.Shared.Commands.CreateFolder;
using CreateUser = Dfs.Shared.Commands.CreateUser;

namespace Dfs.MetadataServer.Store;

/// <summary>Stage 1 <see cref="IMetadataStore"/>: validate, append to the op log with fsync, then apply to SQLite.</summary>
public sealed class MetadataStore : IMetadataStore, IDisposable
{
    private readonly object _gate = new();
    private readonly OpLog _log;
    private readonly SqliteMetadataState _state;
    private readonly int _snapshotIntervalOps;
    private int _opsSinceSnapshot;
    private bool _disposed;

    /// <summary>Directory where snapshot files (<c>snapshot.db</c> and <c>snapshot.meta.json</c>) are stored.</summary>
    public string SnapshotDirectory { get; }

    /// <summary>Gets the sequence number of the last applied op log entry.</summary>
    public long AppliedSeq
    {
        get
        {
            lock (_gate) return _state.AppliedSeq();
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MetadataStore"/> class, restoring from a valid snapshot
    /// when needed and replaying op log lines newer than the SQLite state.
    /// </summary>
    /// <param name="opLogPath">Path of <c>oplog.jsonl</c>.</param>
    /// <param name="sqlitePath">Path of the SQLite file.</param>
    /// <param name="snapshotDir">Optional snapshot directory; defaults to <c>snapshots</c> alongside <paramref name="sqlitePath"/>.</param>
    /// <param name="snapshotIntervalOps">Number of applied operations between automatic snapshots (0 disables).</param>
    public MetadataStore(
        string opLogPath,
        string sqlitePath,
        string? snapshotDir = null,
        int snapshotIntervalOps = MetadataServerConfig.DefaultSnapshotIntervalOps)
    {
        SnapshotDirectory = string.IsNullOrWhiteSpace(snapshotDir)
            ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(sqlitePath))!, "snapshots")
            : Path.GetFullPath(snapshotDir);
        _snapshotIntervalOps = Math.Max(0, snapshotIntervalOps);

        _log = OpLog.Open(opLogPath, out var entries);
        var validSnapshot = SnapshotManager.TryLoadValidSnapshot(SnapshotDirectory, _log.LastSeq);

        var existingSeq = SnapshotManager.TryReadSqliteSeq(sqlitePath);
        if (existingSeq is null || existingSeq.Value > _log.LastSeq)
        {
            SnapshotManager.DeleteSqliteFiles(sqlitePath);
            existingSeq = null;
        }

        if (validSnapshot is not null && (existingSeq is null || existingSeq.Value < validSnapshot.LastSeq))
        {
            SnapshotManager.RestoreSnapshot(validSnapshot.DbPath, sqlitePath);
        }

        _state = new SqliteMetadataState(sqlitePath);
        try
        {
            var applied = _state.AppliedSeq();
            foreach (var e in entries.Where(e => e.Seq > applied)) _state.Apply(e.Seq, e.Command);
        }
        catch
        {
            _state.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Takes an atomic point-in-time snapshot of the current SQLite metadata state and returns its <c>lastSeq</c>.
    /// </summary>
    public long TakeSnapshot()
    {
        lock (_gate)
        {
            return TakeSnapshotLocked();
        }
    }

    /// <inheritdoc />
    public Task<CommandResult> SubmitAsync(IMetadataCommand command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!OpLog.IsSupported(command))
                return Task.FromResult(CommandResult.Fail(ErrorCode.BadRequest, $"Unsupported command {command.GetType().Name}."));
            var rejected = _state.Validate(command);
            if (rejected is not null) return Task.FromResult(rejected);
            var seq = _log.Append(command);
            _state.Apply(seq, command);
            _opsSinceSnapshot++;
            if (_snapshotIntervalOps > 0 && _opsSinceSnapshot >= _snapshotIntervalOps)
            {
                try
                {
                    TakeSnapshotLocked();
                }
                catch
                {
                    // Command is already durably committed in op log and SQLite; do not fail caller or brick subsequent writes.
                    _opsSinceSnapshot = 0;
                }
            }
            return Task.FromResult(CommandResult.Ok(seq));
        }
    }

    /// <inheritdoc />
    public Task<ItemInfo?> FindItemByPathAsync(string path, CancellationToken cancellationToken = default) => Read(() => _state.FindItemByPath(path));

    /// <inheritdoc />
    public Task<ItemInfo?> FindItemByIdAsync(string itemId, CancellationToken cancellationToken = default) => Read(() => _state.FindItemById(itemId));

    /// <inheritdoc />
    public Task<IReadOnlyList<ItemInfo>> ListChildrenAsync(string folderId, CancellationToken cancellationToken = default) => Read(() => _state.ListChildren(folderId));

    /// <inheritdoc />
    public Task<IReadOnlyList<VersionInfo>> ListVersionsAsync(string itemId, CancellationToken cancellationToken = default) =>
        Read(() => _state.ListVersions(itemId));

    /// <inheritdoc />
    public Task<IReadOnlyList<ChunkRecord>> GetVersionChunksAsync(string itemId, int version, CancellationToken cancellationToken = default) =>
        Read(() => _state.GetVersionChunks(itemId, version));

    /// <inheritdoc />
    public Task<UploadRecord?> FindUploadAsync(string uploadId, CancellationToken cancellationToken = default) =>
        Read(() => _state.FindUpload(uploadId));

    /// <inheritdoc />
    public Task<UserCredentials?> FindUserByNameAsync(string username, CancellationToken cancellationToken = default) => Read(() => _state.FindUserByName(username));

    /// <inheritdoc />
    public Task<UserCredentials?> FindUserByIdAsync(string userId, CancellationToken cancellationToken = default) => Read(() => _state.FindUserById(userId));

    /// <inheritdoc />
    public Task<IReadOnlyList<UserInfo>> ListUsersAsync(CancellationToken cancellationToken = default) => Read(() => _state.ListUsers());

    /// <inheritdoc />
    public Task<IReadOnlyList<GroupInfo>> ListGroupsAsync(CancellationToken cancellationToken = default) =>
        Read(() => _state.ListGroups());

    /// <inheritdoc />
    public Task<IReadOnlyList<RoleInfo>> ListRolesAsync(CancellationToken cancellationToken = default) =>
        Read(() => _state.ListRoles());

    /// <inheritdoc />
    public Task<IReadOnlyList<GrantInfo>> ListGrantsAsync(string? itemId, string? subjectId, bool systemOnly, CancellationToken cancellationToken = default) =>
        Read(() => _state.ListGrants(itemId, subjectId, systemOnly));

    /// <inheritdoc />
    public Task<SessionRecord?> FindSessionAsync(string token, CancellationToken cancellationToken = default) => Read(() => _state.FindSession(token));

    /// <inheritdoc />
    public Task<IReadOnlyList<DataServerInfo>> ListDataServersAsync(CancellationToken cancellationToken = default) =>
        Read(() => _state.ListDataServers());

    /// <summary>Marks alive Data Servers as Dead if their last heartbeat was before the cutoff.</summary>
    public int MarkDeadServers(DateTime cutoff)
    {
        lock (_gate)
        {
            return _state.MarkDeadServers(cutoff);
        }
    }

    /// <summary>Finds a registered Data Server by id.</summary>
    public Task<DataServerInfo?> FindDataServerByIdAsync(string nodeId, CancellationToken cancellationToken = default) =>
        Read(() => _state.FindDataServerById(nodeId));

    /// <inheritdoc />
    public Task<IReadOnlyList<AuditEntry>> ListAuditAsync(DateTime? from, DateTime? to, string? userId, int limit, CancellationToken cancellationToken = default) =>
        Read(() => _state.ListAudit(from, to, userId, limit));

    /// <summary>Releases the SQLite file.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _state.Dispose();
        }
    }

    private long TakeSnapshotLocked()
    {
        var lastSeq = _state.AppliedSeq();
        SnapshotManager.WriteSnapshot(_state, SnapshotDirectory, lastSeq);
        _opsSinceSnapshot = 0;
        return lastSeq;
    }

    private Task<T> Read<T>(Func<T> read)
    {
        lock (_gate) return Task.FromResult(read());
    }
}
