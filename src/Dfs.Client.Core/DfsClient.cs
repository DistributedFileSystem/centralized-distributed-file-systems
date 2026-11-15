using System.Net.Sockets;
using Dfs.Shared.Client;
using Dfs.Shared.Framing;
using Dfs.Shared.Messages;

namespace Dfs.Client.Core;

/// <summary>
/// Default implementation of <see cref="IDfsClient"/> communicating with the DFS cluster over TCP.
/// Maintains candidate metadata endpoints, automatic connection and Hello handshake,
/// session caching, and namespace request dispatching.
/// </summary>
public class DfsClient : IDfsClient, IAsyncDisposable, IDisposable
{
    private readonly DfsClientOptions _options;
    private readonly IReadOnlyList<string> _endpoints;
    private readonly SemaphoreSlim _streamLock = new(1, 1);

    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private string? _connectedEndpoint;
    private string? _connectedNodeId;

    private string? _session;
    private UserRef? _currentUser;
    private IReadOnlyList<string>? _systemRoles;
    private IReadOnlyList<string>? _permissions;
    private DateTime? _sessionExpiresAt;

    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of <see cref="DfsClient"/> with the specified options.
    /// </summary>
    /// <param name="options">Client options.</param>
    public DfsClient(DfsClientOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _endpoints = options.MetadataEndpoints.Count > 0
            ? options.MetadataEndpoints
            : new[] { DfsClientOptions.DefaultEndpoint };
    }

    /// <summary>
    /// Initializes a new instance of <see cref="DfsClient"/> targeting the provided metadata endpoints.
    /// </summary>
    /// <param name="endpoints">Sequence of "host:port" endpoints.</param>
    public DfsClient(IEnumerable<string> endpoints)
        : this(new DfsClientOptions { MetadataEndpoints = endpoints?.ToArray() ?? Array.Empty<string>() })
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="DfsClient"/> with a delimited list of endpoints (e.g. "127.0.0.1:7000,127.0.0.1:7001").
    /// </summary>
    /// <param name="endpoint">Delimited endpoint string.</param>
    public DfsClient(string endpoint)
        : this(new DfsClientOptions { MetadataEndpoints = DfsClientOptions.ParseEndpoints(endpoint) })
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="DfsClient"/> reading from <c>METADATA_ENDPOINTS</c> environment variable.
    /// </summary>
    public DfsClient()
        : this(DfsClientOptions.FromEnvironment())
    {
    }

    /// <summary>
    /// True if the client is currently logged in with an active session token.
    /// </summary>
    public bool IsLoggedIn => _session is not null && (!_sessionExpiresAt.HasValue || _sessionExpiresAt.Value > DateTime.UtcNow);

    /// <summary>
    /// Active session token, or null if not logged in.
    /// </summary>
    public string? Session => _session;

    /// <summary>
    /// Currently logged in user reference, or null.
    /// </summary>
    public UserRef? CurrentUser => _currentUser;

    /// <summary>
    /// System roles assigned to the current user, or null.
    /// </summary>
    public IReadOnlyList<string>? SystemRoles => _systemRoles;

    /// <summary>
    /// Permissions granted to the current user, or null.
    /// </summary>
    public IReadOnlyList<string>? Permissions => _permissions;

    /// <summary>
    /// True if the underlying TCP client is connected.
    /// </summary>
    public bool IsConnected => _tcpClient?.Connected == true && _stream is not null;

    /// <summary>
    /// The endpoint string currently connected to, or null.
    /// </summary>
    public string? ConnectedEndpoint => _connectedEndpoint;

    /// <summary>
    /// The node id of the metadata server currently connected to, or null.
    /// </summary>
    public string? ConnectedNodeId => _connectedNodeId;

    /// <summary>
    /// Connects to one of the configured <see cref="DfsClientOptions.MetadataEndpoints"/>,
    /// executing the Hello handshake. If already connected, this is a no-op.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (IsConnected)
        {
            return;
        }

        DisconnectInternal();

        List<Exception> errors = new();

        foreach (var endpoint in _endpoints)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TcpClient? tcpClient = null;
            try
            {
                var (host, port) = ParseEndpoint(endpoint);

                tcpClient = new TcpClient();
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(_options.ConnectTimeout);

                await tcpClient.ConnectAsync(host, port, timeoutCts.Token).ConfigureAwait(false);
                var stream = tcpClient.GetStream();

                var hello = new Hello(Guid.NewGuid().ToString("D"), 1, NodeType.Client);
                var response = await HandshakeHandler.ClientHandshakeAsync(stream, hello, timeoutCts.Token).ConfigureAwait(false);

                if (response.NodeType != NodeType.Metadata)
                {
                    throw new DfsException(
                        ErrorCode.BadRequest,
                        $"Connected node '{response.NodeId}' at {endpoint} is {response.NodeType}, expected Metadata server.");
                }

                _tcpClient = tcpClient;
                _stream = stream;
                _connectedEndpoint = endpoint;
                _connectedNodeId = response.NodeId;
                return;
            }
            catch (Exception ex) when (ex is SocketException or IOException or DfsException or OperationCanceledException)
            {
                tcpClient?.Dispose();
                errors.Add(new InvalidOperationException($"Endpoint '{endpoint}' failed: {ex.Message}", ex));
            }
        }

        throw new DfsException(
            ErrorCode.Unavailable,
            $"Could not connect to any metadata server endpoint ({string.Join(", ", _endpoints)}).");
    }

    /// <inheritdoc />
    public async Task<LoginResponse> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(username))
        {
            throw new DfsException(ErrorCode.BadRequest, "Username cannot be empty.");
        }

        var req = new Login(Guid.NewGuid().ToString("D"), username, password);
        var res = await SendAndReceiveAsync<Login, LoginResponse>(
            MessageType.Login, MessageType.LoginResponse, req, cancellationToken).ConfigureAwait(false);

        _session = res.Session;
        _currentUser = res.User;
        _systemRoles = res.SystemRoles;
        _permissions = res.Permissions;
        _sessionExpiresAt = res.ExpiresAt;

        return res;
    }

    /// <inheritdoc />
    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_session is null)
        {
            return;
        }

        try
        {
            if (IsConnected)
            {
                var req = new Logout(Guid.NewGuid().ToString("D"), _session);
                await SendAndReceiveAsync<Logout, LogoutResponse>(
                    MessageType.Logout, MessageType.LogoutResponse, req, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (DfsException)
        {
            // Ignore server-side logout failures (e.g. session already deleted/expired)
        }
        finally
        {
            ClearSession();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ItemInfo>> ListDirAsync(string path, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureLoggedIn();

        var req = new ListDir(Guid.NewGuid().ToString("D"), _session!, path);
        var res = await SendAndReceiveAsync<ListDir, ListDirResponse>(
            MessageType.ListDir, MessageType.ListDirResponse, req, cancellationToken).ConfigureAwait(false);
        return res.Items;
    }

    /// <inheritdoc />
    public async Task<ItemInfo> StatAsync(string path, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureLoggedIn();

        var req = new Stat(Guid.NewGuid().ToString("D"), _session!, path);
        var res = await SendAndReceiveAsync<Stat, StatResponse>(
            MessageType.Stat, MessageType.StatResponse, req, cancellationToken).ConfigureAwait(false);
        return res.Item;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VersionInfo>> ListVersionsAsync(string path, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureLoggedIn();

        var req = new ListVersions(Guid.NewGuid().ToString("D"), _session!, path);
        var res = await SendAndReceiveAsync<ListVersions, ListVersionsResponse>(
            MessageType.ListVersions, MessageType.ListVersionsResponse, req, cancellationToken).ConfigureAwait(false);
        return res.Versions;
    }

    /// <inheritdoc />
    public async Task<ItemInfo> CreateFolderAsync(string path, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureLoggedIn();

        var req = new CreateFolder(Guid.NewGuid().ToString("D"), _session!, path);
        var res = await SendAndReceiveAsync<CreateFolder, CreateFolderResponse>(
            MessageType.CreateFolder, MessageType.CreateFolderResponse, req, cancellationToken).ConfigureAwait(false);
        return res.Item;
    }

    /// <inheritdoc />
    public async Task<ItemInfo> RenameAsync(string path, string newName, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureLoggedIn();

        var req = new Rename(Guid.NewGuid().ToString("D"), _session!, path, newName);
        var res = await SendAndReceiveAsync<Rename, RenameResponse>(
            MessageType.Rename, MessageType.RenameResponse, req, cancellationToken).ConfigureAwait(false);
        return res.Item;
    }

    /// <inheritdoc />
    public async Task<ItemInfo> MoveAsync(string path, string newParentPath, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureLoggedIn();

        var req = new Move(Guid.NewGuid().ToString("D"), _session!, path, newParentPath);
        var res = await SendAndReceiveAsync<Move, MoveResponse>(
            MessageType.Move, MessageType.MoveResponse, req, cancellationToken).ConfigureAwait(false);
        return res.Item;
    }

    /// <inheritdoc />
    public async Task DeleteItemAsync(string path, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureLoggedIn();

        var req = new DeleteItem(Guid.NewGuid().ToString("D"), _session!, path);
        await SendAndReceiveAsync<DeleteItem, DeleteItemResponse>(
            MessageType.DeleteItem, MessageType.DeleteItemResponse, req, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DataServerInfo>> ClusterStatusAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureLoggedIn();

        var req = new ClusterStatus(Guid.NewGuid().ToString("D"), _session!);
        var res = await SendAndReceiveAsync<ClusterStatus, ClusterStatusResponse>(
            MessageType.ClusterStatus, MessageType.ClusterStatusResponse, req, cancellationToken).ConfigureAwait(false);
        return res.DataServers;
    }

    /// <inheritdoc />
    public Task<CommitUploadResponse> UploadFileAsync(string path, Stream content, int baseVersion, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("UploadFileAsync is not supported yet (scheduled for M2-10).");

    /// <inheritdoc />
    public Task DownloadFileAsync(string path, Stream destination, int version = 0, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("DownloadFileAsync is not supported yet (scheduled for M2-10).");

    /// <inheritdoc />
    public Task<UserInfo> CreateUserAsync(string username, string password, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Admin operations are scheduled for M2-13.");

    /// <inheritdoc />
    public Task<IReadOnlyList<UserInfo>> ListUsersAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Admin operations are scheduled for M2-13.");

    /// <inheritdoc />
    public Task SetUserDisabledAsync(string userId, bool disabled, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Admin operations are scheduled for M2-13.");

    /// <inheritdoc />
    public Task ResetPasswordAsync(string userId, string newPassword, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Admin operations are scheduled for M2-13.");

    /// <inheritdoc />
    public Task<GroupInfo> CreateGroupAsync(string name, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Admin operations are scheduled for M2-13.");

    /// <inheritdoc />
    public Task<IReadOnlyList<GroupInfo>> ListGroupsAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Admin operations are scheduled for M2-13.");

    /// <inheritdoc />
    public Task SetGroupMemberAsync(string groupId, string userId, bool member, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Admin operations are scheduled for M2-13.");

    /// <inheritdoc />
    public Task<RoleInfo> CreateRoleAsync(string name, RoleScope scope, IReadOnlyList<string> permissions, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Admin operations are scheduled for M2-13.");

    /// <inheritdoc />
    public Task<IReadOnlyList<RoleInfo>> ListRolesAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Admin operations are scheduled for M2-13.");

    /// <inheritdoc />
    public Task<RoleInfo> UpdateRoleAsync(string roleId, IReadOnlyList<string> permissions, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Admin operations are scheduled for M2-13.");

    /// <inheritdoc />
    public Task<GrantInfo> CreateGrantAsync(string roleId, SubjectType subjectType, string subjectId, string? path = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Admin operations are scheduled for M2-13.");

    /// <inheritdoc />
    public Task<IReadOnlyList<GrantInfo>> ListGrantsAsync(string? path = null, string? subjectId = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Admin operations are scheduled for M2-13.");

    /// <inheritdoc />
    public Task RevokeGrantAsync(string grantId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Admin operations are scheduled for M2-13.");

    /// <inheritdoc />
    public Task<IReadOnlyList<AuditEntry>> ListAuditAsync(DateTime? from = null, DateTime? to = null, string? userId = null, int limit = 100, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Admin operations are scheduled for M2-13.");

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (IsConnected)
        {
            return;
        }

        await ConnectAsync(cancellationToken).ConfigureAwait(false);
    }

    private void EnsureLoggedIn()
    {
        if (_session is null)
        {
            throw new DfsException(ErrorCode.Unauthorized, "Client is not logged in.");
        }

        if (_sessionExpiresAt.HasValue && _sessionExpiresAt.Value <= DateTime.UtcNow)
        {
            ClearSession();
            throw new DfsException(ErrorCode.Unauthorized, "Session has expired.");
        }
    }

    private void ClearSession()
    {
        _session = null;
        _currentUser = null;
        _systemRoles = null;
        _permissions = null;
        _sessionExpiresAt = null;
    }

    private async Task<TRes> SendAndReceiveAsync<TReq, TRes>(
        MessageType requestType,
        MessageType expectedResponseType,
        TReq request,
        CancellationToken cancellationToken)
        where TReq : class
        where TRes : class
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(_options.RequestTimeout);
        var token = linkedCts.Token;

        await _streamLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await EnsureConnectedAsync(token).ConfigureAwait(false);

            await FrameCodec.WriteMessageAsync(_stream!, requestType, request, token).ConfigureAwait(false);

            var rawFrame = await FrameCodec.ReadRawFrameAsync(_stream!, token).ConfigureAwait(false)
                ?? throw new DfsException(ErrorCode.Unavailable, "Connection closed by metadata server.");

            if (rawFrame.Type == MessageType.Error)
            {
                var error = FrameSerialization.DeserializeJson<ErrorResponse>(rawFrame.Payload);
                if (error.Code == ErrorCode.Unauthorized)
                {
                    ClearSession();
                }

                throw new DfsException(error.Code, error.Message, error.Details);
            }

            if (rawFrame.Type != expectedResponseType)
            {
                throw new DfsException(
                    ErrorCode.BadRequest,
                    $"Expected response type {expectedResponseType} (0x{(byte)expectedResponseType:X2}) but received {rawFrame.Type} (0x{(byte)rawFrame.Type:X2}).");
            }

            return FrameSerialization.DeserializeJson<TRes>(rawFrame.Payload);
        }
        catch (DfsException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            DisconnectInternal();
            throw new DfsException(ErrorCode.Unavailable, "Request to metadata server timed out.");
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            DisconnectInternal();
            throw new DfsException(ErrorCode.Unavailable, "Network error communicating with metadata server: " + ex.Message);
        }
        finally
        {
            _streamLock.Release();
        }
    }

    private void DisconnectInternal()
    {
        try
        {
            _stream?.Dispose();
        }
        catch
        {
        }

        try
        {
            _tcpClient?.Dispose();
        }
        catch
        {
        }

        _stream = null;
        _tcpClient = null;
        _connectedEndpoint = null;
        _connectedNodeId = null;
    }

    private static (string Host, int Port) ParseEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ArgumentException("Endpoint cannot be null or empty.", nameof(endpoint));
        }

        if (endpoint.StartsWith('['))
        {
            var closingBracket = endpoint.IndexOf(']');
            if (closingBracket < 0 || closingBracket + 2 >= endpoint.Length || endpoint[closingBracket + 1] != ':')
            {
                throw new ArgumentException($"Invalid IPv6 endpoint format '{endpoint}'.", nameof(endpoint));
            }

            var host = endpoint.Substring(1, closingBracket - 1);
            if (!int.TryParse(endpoint.Substring(closingBracket + 2), out var p))
            {
                throw new ArgumentException($"Invalid port in endpoint '{endpoint}'.", nameof(endpoint));
            }

            return (host, p);
        }

        var parts = endpoint.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var port))
        {
            throw new ArgumentException($"Invalid endpoint format '{endpoint}'. Expected 'host:port'.", nameof(endpoint));
        }

        return (parts[0], port);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);
        Dispose(false);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (disposing)
        {
            DisconnectInternal();
            _streamLock.Dispose();
        }
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (_stream is not null)
        {
            try
            {
                await _stream.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
            }
        }

        DisconnectInternal();
        _streamLock.Dispose();
    }
}
