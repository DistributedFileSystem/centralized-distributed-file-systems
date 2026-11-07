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

    /// <summary>Initializes a new instance of the <see cref="MetadataStore"/> class and replays op log lines newer than the SQLite state.</summary>
    /// <param name="opLogPath">Path of <c>oplog.jsonl</c>.</param>
    /// <param name="sqlitePath">Path of the SQLite file.</param>
    public MetadataStore(string opLogPath, string sqlitePath)
    {
        _log = OpLog.Open(opLogPath, out var entries);
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
        Task.FromResult<IReadOnlyList<VersionInfo>>(Array.Empty<VersionInfo>());

    /// <inheritdoc />
    public Task<IReadOnlyList<ChunkRecord>> GetVersionChunksAsync(string itemId, int version, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ChunkRecord>>(Array.Empty<ChunkRecord>());

    /// <inheritdoc />
    public Task<UploadRecord?> FindUploadAsync(string uploadId, CancellationToken cancellationToken = default) => Task.FromResult<UploadRecord?>(null);

    /// <inheritdoc />
    public Task<UserCredentials?> FindUserByNameAsync(string username, CancellationToken cancellationToken = default) => Read(() => _state.FindUserByName(username));

    /// <inheritdoc />
    public Task<UserCredentials?> FindUserByIdAsync(string userId, CancellationToken cancellationToken = default) => Read(() => _state.FindUserById(userId));

    /// <inheritdoc />
    public Task<IReadOnlyList<UserInfo>> ListUsersAsync(CancellationToken cancellationToken = default) => Read(() => _state.ListUsers());

    /// <inheritdoc />
    public Task<IReadOnlyList<GroupInfo>> ListGroupsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GroupInfo>>(Array.Empty<GroupInfo>());

    /// <inheritdoc />
    public Task<IReadOnlyList<RoleInfo>> ListRolesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RoleInfo>>(Array.Empty<RoleInfo>());

    /// <inheritdoc />
    public Task<IReadOnlyList<GrantInfo>> ListGrantsAsync(string? itemId, string? subjectId, bool systemOnly, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GrantInfo>>(Array.Empty<GrantInfo>());

    /// <inheritdoc />
    public Task<SessionRecord?> FindSessionAsync(string token, CancellationToken cancellationToken = default) => Task.FromResult<SessionRecord?>(null);

    /// <inheritdoc />
    public Task<IReadOnlyList<DataServerInfo>> ListDataServersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DataServerInfo>>(Array.Empty<DataServerInfo>());

    /// <inheritdoc />
    public Task<IReadOnlyList<AuditEntry>> ListAuditAsync(DateTime? from, DateTime? to, string? userId, int limit, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AuditEntry>>(Array.Empty<AuditEntry>());

    /// <summary>Releases the SQLite file.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _state.Dispose();
        }
    }

    private Task<T> Read<T>(Func<T> read)
    {
        lock (_gate) return Task.FromResult(read());
    }
}
