using System.Net.Sockets;
using System.Text.Json;
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
    private readonly object _sessionLock = new();

    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private string? _connectedEndpoint;
    private string? _connectedNodeId;

    private string? _session;
    private UserRef? _currentUser;
    private IReadOnlyList<string>? _systemRoles;
    private IReadOnlyList<string>? _permissions;
    private DateTime? _sessionExpiresAt;
    private bool _sessionExpired;

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
    public bool IsLoggedIn
    {
        get
        {
            lock (_sessionLock)
            {
                CheckSessionExpiryLocked();
                return _session is not null;
            }
        }
    }

    /// <summary>
    /// Active session token, or null if not logged in.
    /// </summary>
    public string? Session
    {
        get
        {
            lock (_sessionLock)
            {
                CheckSessionExpiryLocked();
                return _session;
            }
        }
    }

    /// <summary>
    /// Currently logged in user reference, or null.
    /// </summary>
    public UserRef? CurrentUser
    {
        get
        {
            lock (_sessionLock)
            {
                CheckSessionExpiryLocked();
                return _currentUser;
            }
        }
    }

    /// <summary>
    /// System roles assigned to the current user, or null.
    /// </summary>
    public IReadOnlyList<string>? SystemRoles
    {
        get
        {
            lock (_sessionLock)
            {
                CheckSessionExpiryLocked();
                return _systemRoles;
            }
        }
    }

    /// <summary>
    /// Permissions granted to the current user, or null.
    /// </summary>
    public IReadOnlyList<string>? Permissions
    {
        get
        {
            lock (_sessionLock)
            {
                CheckSessionExpiryLocked();
                return _permissions;
            }
        }
    }

    /// <summary>
    /// True if the underlying TCP client is connected.
    /// </summary>
    public bool IsConnected => _tcpClient is not null && _stream is not null && IsSocketConnected(_tcpClient);

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

        await _streamLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ConnectInternalAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _streamLock.Release();
        }
    }

    private async Task ConnectInternalAsync(CancellationToken cancellationToken)
    {
        if (IsConnected)
        {
            return;
        }

        DisconnectInternal();

        List<Exception> errors = new();
        DfsException? versionMismatchError = null;
        DfsException? badRequestError = null;

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

                var helloReqId = Guid.NewGuid().ToString("D");
                var hello = new Hello(helloReqId, ProtocolConstants.CurrentProtocolVersion, NodeType.Client);
                var response = await HandshakeHandler.ClientHandshakeAsync(stream, hello, timeoutCts.Token).ConfigureAwait(false);

                if (response.ProtocolVersion != ProtocolConstants.CurrentProtocolVersion)
                {
                    throw new DfsException(
                        ErrorCode.VersionMismatch,
                        $"Connected node '{response.NodeId}' protocol version {response.ProtocolVersion} does not match expected {ProtocolConstants.CurrentProtocolVersion}.");
                }

                if (response.RequestId != helloReqId)
                {
                    throw new DfsException(
                        ErrorCode.BadRequest,
                        $"Hello response RequestId mismatch: expected '{helloReqId}', got '{response.RequestId}'.");
                }

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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                tcpClient?.Dispose();
                throw;
            }
            catch (DfsException ex) when (ex.Code == ErrorCode.VersionMismatch)
            {
                tcpClient?.Dispose();
                versionMismatchError ??= ex;
                errors.Add(ex);
            }
            catch (DfsException ex) when (ex.Code == ErrorCode.BadRequest)
            {
                tcpClient?.Dispose();
                badRequestError ??= ex;
                errors.Add(ex);
            }
            catch (Exception ex)
            {
                tcpClient?.Dispose();
                errors.Add(new InvalidOperationException($"Endpoint '{endpoint}' failed: {ex.Message}", ex));
            }
        }

        if (versionMismatchError is not null)
        {
            throw versionMismatchError;
        }

        if (badRequestError is not null)
        {
            throw badRequestError;
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

        var reqId = Guid.NewGuid().ToString("D");
        return await SendAndReceiveAsync<Login, LoginResponse>(
            reqId,
            MessageType.Login,
            MessageType.LoginResponse,
            _ => new Login(reqId, username, password),
            requiresSession: false,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(_options.RequestTimeout);
        var token = linkedCts.Token;

        await _streamLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            string? sessionToRevoke;
            lock (_sessionLock)
            {
                sessionToRevoke = _session;
            }

            if (sessionToRevoke is null)
            {
                return;
            }

            try
            {
                await EnsureConnectedInternalAsync(token).ConfigureAwait(false);

                var reqId = Guid.NewGuid().ToString("D");
                var req = new Logout(reqId, sessionToRevoke);

                await FrameCodec.WriteMessageAsync(_stream!, MessageType.Logout, req, token).ConfigureAwait(false);
                var rawFrame = await FrameCodec.ReadRawFrameAsync(_stream!, token).ConfigureAwait(false);
                if (rawFrame is null)
                {
                    DisconnectInternal();
                }
                else if (rawFrame.Type != MessageType.LogoutResponse && rawFrame.Type != MessageType.Error)
                {
                    DisconnectInternal();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                DisconnectInternal();
                throw;
            }
            catch (Exception)
            {
                // Disconnect on any failure/timeout during logout to ensure no dirty bytes remain on the socket
                DisconnectInternal();
            }
            finally
            {
                lock (_sessionLock)
                {
                    ClearSessionLocked(expired: false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        finally
        {
            _streamLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ItemInfo>> ListDirAsync(string path, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var reqId = Guid.NewGuid().ToString("D");
        var res = await SendAndReceiveAsync<ListDir, ListDirResponse>(
            reqId,
            MessageType.ListDir,
            MessageType.ListDirResponse,
            session => new ListDir(reqId, session!, path),
            requiresSession: true,
            cancellationToken).ConfigureAwait(false);
        return res.Items;
    }

    /// <inheritdoc />
    public async Task<ItemInfo> StatAsync(string path, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var reqId = Guid.NewGuid().ToString("D");
        var res = await SendAndReceiveAsync<Stat, StatResponse>(
            reqId,
            MessageType.Stat,
            MessageType.StatResponse,
            session => new Stat(reqId, session!, path),
            requiresSession: true,
            cancellationToken).ConfigureAwait(false);
        return res.Item;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VersionInfo>> ListVersionsAsync(string path, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var reqId = Guid.NewGuid().ToString("D");
        var res = await SendAndReceiveAsync<ListVersions, ListVersionsResponse>(
            reqId,
            MessageType.ListVersions,
            MessageType.ListVersionsResponse,
            session => new ListVersions(reqId, session!, path),
            requiresSession: true,
            cancellationToken).ConfigureAwait(false);
        return res.Versions;
    }

    /// <inheritdoc />
    public async Task<ItemInfo> CreateFolderAsync(string path, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var reqId = Guid.NewGuid().ToString("D");
        var res = await SendAndReceiveAsync<CreateFolder, CreateFolderResponse>(
            reqId,
            MessageType.CreateFolder,
            MessageType.CreateFolderResponse,
            session => new CreateFolder(reqId, session!, path),
            requiresSession: true,
            cancellationToken).ConfigureAwait(false);
        return res.Item;
    }

    /// <inheritdoc />
    public async Task<ItemInfo> RenameAsync(string path, string newName, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var reqId = Guid.NewGuid().ToString("D");
        var res = await SendAndReceiveAsync<Rename, RenameResponse>(
            reqId,
            MessageType.Rename,
            MessageType.RenameResponse,
            session => new Rename(reqId, session!, path, newName),
            requiresSession: true,
            cancellationToken).ConfigureAwait(false);
        return res.Item;
    }

    /// <inheritdoc />
    public async Task<ItemInfo> MoveAsync(string path, string newParentPath, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var reqId = Guid.NewGuid().ToString("D");
        var res = await SendAndReceiveAsync<Move, MoveResponse>(
            reqId,
            MessageType.Move,
            MessageType.MoveResponse,
            session => new Move(reqId, session!, path, newParentPath),
            requiresSession: true,
            cancellationToken).ConfigureAwait(false);
        return res.Item;
    }

    /// <inheritdoc />
    public async Task DeleteItemAsync(string path, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var reqId = Guid.NewGuid().ToString("D");
        await SendAndReceiveAsync<DeleteItem, DeleteItemResponse>(
            reqId,
            MessageType.DeleteItem,
            MessageType.DeleteItemResponse,
            session => new DeleteItem(reqId, session!, path),
            requiresSession: true,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DataServerInfo>> ClusterStatusAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var reqId = Guid.NewGuid().ToString("D");
        var res = await SendAndReceiveAsync<ClusterStatus, ClusterStatusResponse>(
            reqId,
            MessageType.ClusterStatus,
            MessageType.ClusterStatusResponse,
            session => new ClusterStatus(reqId, session!),
            requiresSession: true,
            cancellationToken).ConfigureAwait(false);
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

    private async Task EnsureConnectedInternalAsync(CancellationToken cancellationToken)
    {
        if (IsConnected)
        {
            return;
        }

        await ConnectInternalAsync(cancellationToken).ConfigureAwait(false);
    }

    private void EnsureLoggedInLocked()
    {
        lock (_sessionLock)
        {
            CheckSessionExpiryLocked();

            if (_sessionExpired)
            {
                throw new DfsException(ErrorCode.Unauthorized, "Session has expired.");
            }

            if (_session is null)
            {
                throw new DfsException(ErrorCode.Unauthorized, "Client is not logged in.");
            }
        }
    }

    private void CheckSessionExpiryLocked()
    {
        if (_session is not null && _sessionExpiresAt.HasValue && _sessionExpiresAt.Value <= DateTime.UtcNow)
        {
            ClearSessionLocked(expired: true);
        }
    }

    private void ClearSessionLocked(bool expired = false)
    {
        _session = null;
        _currentUser = null;
        _systemRoles = null;
        _permissions = null;
        _sessionExpiresAt = null;
        _sessionExpired = expired;
    }

    private async Task<TRes> SendAndReceiveAsync<TReq, TRes>(
        string requestId,
        MessageType requestType,
        MessageType expectedResponseType,
        Func<string?, TReq> requestFactory,
        bool requiresSession,
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
            if (requiresSession)
            {
                EnsureLoggedInLocked();
            }

            string? session;
            lock (_sessionLock)
            {
                session = _session;
            }

            var request = requestFactory(session);

            await EnsureConnectedInternalAsync(token).ConfigureAwait(false);

            try
            {
                await FrameCodec.WriteMessageAsync(_stream!, requestType, request, token).ConfigureAwait(false);
            }
            catch
            {
                DisconnectInternal();
                throw;
            }

            RawFrame? rawFrame;
            try
            {
                rawFrame = await FrameCodec.ReadRawFrameAsync(_stream!, token).ConfigureAwait(false);
            }
            catch
            {
                DisconnectInternal();
                throw;
            }

            if (rawFrame is null)
            {
                DisconnectInternal();
                throw new DfsException(ErrorCode.Unavailable, "Connection closed by metadata server (EOF).");
            }

            if (rawFrame.Type == MessageType.Error)
            {
                ErrorResponse error;
                try
                {
                    error = FrameSerialization.DeserializeJson<ErrorResponse>(rawFrame.Payload);
                }
                catch
                {
                    DisconnectInternal();
                    throw;
                }

                if (requiresSession && error.Code == ErrorCode.Unauthorized)
                {
                    lock (_sessionLock)
                    {
                        ClearSessionLocked(expired: true);
                    }
                }

                throw new DfsException(error.Code, error.Message, error.Details);
            }

            if (rawFrame.Type != expectedResponseType)
            {
                DisconnectInternal();
                throw new DfsException(
                    ErrorCode.BadRequest,
                    $"Expected response type {expectedResponseType} (0x{(byte)expectedResponseType:X2}) but received {rawFrame.Type} (0x{(byte)rawFrame.Type:X2}).");
            }

            var respRequestId = ExtractRequestId(rawFrame.Payload);
            if (respRequestId is not null && respRequestId != requestId)
            {
                DisconnectInternal();
                throw new DfsException(
                    ErrorCode.BadRequest,
                    $"RequestId mismatch: expected '{requestId}' but received '{respRequestId}'.");
            }

            TRes response;
            try
            {
                response = FrameSerialization.DeserializeJson<TRes>(rawFrame.Payload);
            }
            catch
            {
                DisconnectInternal();
                throw;
            }

            if (response is LoginResponse loginRes)
            {
                lock (_sessionLock)
                {
                    _session = loginRes.Session;
                    _currentUser = loginRes.User;
                    _systemRoles = loginRes.SystemRoles;
                    _permissions = loginRes.Permissions;
                    _sessionExpiresAt = loginRes.ExpiresAt;
                    _sessionExpired = false;
                }
            }

            return response;
        }
        catch (DfsException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            DisconnectInternal();
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
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

    private static string? ExtractRequestId(ReadOnlySpan<byte> payload)
    {
        try
        {
            var reader = new Utf8JsonReader(payload);
            using var doc = JsonDocument.ParseValue(ref reader);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("requestId", out var prop) && prop.ValueKind == JsonValueKind.String)
                {
                    return prop.GetString();
                }

                if (doc.RootElement.TryGetProperty("RequestId", out prop) && prop.ValueKind == JsonValueKind.String)
                {
                    return prop.GetString();
                }
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private void DisconnectInternal()
    {
        var stream = Interlocked.Exchange(ref _stream, null);
        var tcp = Interlocked.Exchange(ref _tcpClient, null);
        _connectedEndpoint = null;
        _connectedNodeId = null;

        try
        {
            stream?.Dispose();
        }
        catch
        {
        }

        try
        {
            tcp?.Dispose();
        }
        catch
        {
        }
    }

    private static bool IsSocketConnected(TcpClient? client)
    {
        if (client is null || !client.Connected)
        {
            return false;
        }

        try
        {
            var socket = client.Client;
            if (socket is null || !socket.Connected)
            {
                return false;
            }

            return !(socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0);
        }
        catch
        {
            return false;
        }
    }

    public static (string Host, int Port) ParseEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ArgumentException("Endpoint cannot be null or empty.", nameof(endpoint));
        }

        endpoint = endpoint.Trim();

        string host;
        int port;

        if (endpoint.StartsWith('['))
        {
            var closingBracket = endpoint.IndexOf(']');
            if (closingBracket < 0 || closingBracket + 2 >= endpoint.Length || endpoint[closingBracket + 1] != ':')
            {
                throw new ArgumentException($"Invalid IPv6 endpoint format '{endpoint}'.", nameof(endpoint));
            }

            host = endpoint.Substring(1, closingBracket - 1).Trim();
            if (string.IsNullOrEmpty(host))
            {
                throw new ArgumentException($"IPv6 host cannot be empty in endpoint '{endpoint}'.", nameof(endpoint));
            }

            if (!int.TryParse(endpoint.Substring(closingBracket + 2), out port) || port is < 1 or > 65535)
            {
                throw new ArgumentException($"Invalid port in endpoint '{endpoint}'. Port must be between 1 and 65535.", nameof(endpoint));
            }

            return (host, port);
        }

        var colonIdx = endpoint.LastIndexOf(':');
        if (colonIdx <= 0 || colonIdx >= endpoint.Length - 1)
        {
            throw new ArgumentException($"Invalid endpoint format '{endpoint}'. Expected 'host:port'.", nameof(endpoint));
        }

        host = endpoint.Substring(0, colonIdx).Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException($"Host cannot be empty in endpoint '{endpoint}'.", nameof(endpoint));
        }

        if (!int.TryParse(endpoint.Substring(colonIdx + 1), out port) || port is < 1 or > 65535)
        {
            throw new ArgumentException($"Invalid port in endpoint '{endpoint}'. Port must be between 1 and 65535.", nameof(endpoint));
        }

        return (host, port);
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
        if (_disposed)
        {
            return;
        }

        _disposed = true;

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
