using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Dfs.MetadataServer.Config;
using Dfs.MetadataServer.Handlers;
using Dfs.MetadataServer.Store;
using Dfs.Shared.Framing;
using Dfs.Shared.Messages;
using Dfs.Shared.Metadata;
using Dfs.Shared.Security;

namespace Dfs.MetadataServer.Hosting;

/// <summary>
/// TCP server host for the Metadata Server that listens on LISTEN_PORT,
/// executes the Hello handshake on incoming connections, and handles control requests against the metadata store.
/// </summary>
public sealed class MetadataServerHost : IAsyncDisposable
{
    private readonly bool _ownsStore;
    private readonly NamespaceHandler _namespaceHandler;
    private readonly ConcurrentDictionary<Task, bool> _activeConnections = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoopTask;

    /// <summary>Server configuration.</summary>
    public MetadataServerConfig Config { get; }

    /// <summary>The metadata store backing this server instance.</summary>
    public IMetadataStore Store { get; }

    /// <summary>Actual TCP port bound by the listener.</summary>
    public int BoundPort { get; private set; }

    /// <summary>True if the server is currently listening.</summary>
    public bool IsRunning => _listener is not null;

    /// <summary>
    /// Initializes a new instance of <see cref="MetadataServerHost"/>.
    /// </summary>
    /// <param name="config">The configuration parameters.</param>
    /// <param name="store">Optional existing store instance; if null, creates a default MetadataStore.</param>
    /// <param name="authorizer">Optional authorizer used to enforce RBAC permissions on incoming requests.</param>
    public MetadataServerHost(MetadataServerConfig config, IMetadataStore? store = null, IAuthorizer? authorizer = null)
    {
        Config = config;
        if (store is not null)
        {
            Store = store;
            _ownsStore = false;
        }
        else
        {
            Directory.CreateDirectory(config.DataDir);
            Store = new MetadataStore(config.OpLogPath, config.DatabasePath);
            _ownsStore = true;
        }

        _namespaceHandler = new NamespaceHandler(Store, authorizer);
    }

    /// <summary>
    /// Starts the TCP listener on the configured port and begins accepting connections.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_listener is not null)
        {
            throw new InvalidOperationException("MetadataServerHost is already running.");
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener = new TcpListener(IPAddress.Any, Config.ListenPort);
        _listener.Start();

        BoundPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cts.Token), CancellationToken.None);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Runs the server host until the cancellation token is cancelled.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to signal shutdown.</param>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await StartAsync(cancellationToken).ConfigureAwait(false);
        await WaitForShutdownAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Awaits server shutdown.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task WaitForShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (_acceptLoopTask is null)
        {
            return;
        }

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _cts?.Token ?? CancellationToken.None);

            await Task.Delay(Timeout.Infinite, linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }

        await StopAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Stops the TCP listener and waits for active connections to drain.
    /// </summary>
    public async Task StopAsync()
    {
        if (_cts is not null && !_cts.IsCancellationRequested)
        {
            _cts.Cancel();
        }

        _listener?.Stop();

        if (_acceptLoopTask is not null)
        {
            try
            {
                await _acceptLoopTask.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Ignore accept loop shutdown errors
            }
        }

        var activeTasks = _activeConnections.Keys.ToArray();
        if (activeTasks.Length > 0)
        {
            try
            {
                await Task.WhenAll(activeTasks).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Ignore connection errors on shutdown
            }
        }

        _listener = null;
        _acceptLoopTask = null;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener is not null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                var connectionTask = Task.Run(() => HandleConnectionAsync(client, cancellationToken), CancellationToken.None);

                _activeConnections.TryAdd(connectionTask, true);
                _ = connectionTask.ContinueWith(t => _activeConnections.TryRemove(t, out _), TaskScheduler.Default);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception)
            {
                if (cancellationToken.IsCancellationRequested) break;
            }
        }
    }

    private async Task HandleConnectionAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        await using (var stream = client.GetStream())
        {
            try
            {
                var hello = await HandshakeHandler.ServerHandshakeAsync(
                    stream,
                    Config.NodeId,
                    NodeType.Metadata,
                    cancellationToken).ConfigureAwait(false);

                if (hello is null)
                {
                    return;
                }

                // Keep reading and dispatching request frames until the client closes the connection or cancellation is triggered.
                while (!cancellationToken.IsCancellationRequested)
                {
                    RawFrame? frame;
                    try
                    {
                        frame = await FrameCodec.ReadRawFrameAsync(stream, cancellationToken).ConfigureAwait(false);
                    }
                    catch (DfsException ex)
                    {
                        await WriteErrorAsync(stream, Guid.NewGuid().ToString("D"), ex.Code, ex.Message, ex.Details, cancellationToken).ConfigureAwait(false);
                        break;
                    }

                    if (frame is null)
                    {
                        break;
                    }

                    await DispatchFrameAsync(stream, frame, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (DfsException)
            {
                // HandshakeHandler writes the ErrorResponse frame before throwing.
            }
            catch (OperationCanceledException)
            {
                // Server is shutting down
            }
            catch (IOException)
            {
                // Client dropped connection
            }
            catch (SocketException)
            {
                // Client dropped connection
            }
        }
    }

    private async Task DispatchFrameAsync(Stream stream, RawFrame frame, CancellationToken cancellationToken)
    {
        try
        {
            if (NamespaceHandler.CanHandle(frame.Type))
            {
                var response = await _namespaceHandler.HandleAsync(frame, cancellationToken).ConfigureAwait(false);
                await FrameCodec.WriteRawFrameAsync(stream, response.Type, response.Payload, cancellationToken).ConfigureAwait(false);
                return;
            }

            throw new DfsException(ErrorCode.BadRequest, $"Unsupported message type {frame.Type} (0x{(byte)frame.Type:X2}).");
        }
        catch (DfsException ex)
        {
            await WriteErrorAsync(stream, ExtractRequestId(frame.Payload), ex.Code, ex.Message, ex.Details, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            await WriteErrorAsync(stream, ExtractRequestId(frame.Payload), ErrorCode.Internal, "Internal server error.", null, cancellationToken).ConfigureAwait(false);
        }
    }

    private static Task WriteErrorAsync(
        Stream stream,
        string requestId,
        ErrorCode code,
        string message,
        IReadOnlyDictionary<string, JsonElement>? details,
        CancellationToken cancellationToken)
    {
        var copy = details is null
            ? null
            : details as Dictionary<string, JsonElement> ?? new Dictionary<string, JsonElement>(details);
        var error = new ErrorResponse(requestId, code, message, copy);
        return FrameCodec.WriteMessageAsync(stream, MessageType.Error, error, cancellationToken);
    }

    private static string ExtractRequestId(ReadOnlySpan<byte> payload)
    {
        try
        {
            var reader = new Utf8JsonReader(payload);
            using var doc = JsonDocument.ParseValue(ref reader);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("requestId", out var prop) &&
                prop.ValueKind == JsonValueKind.String &&
                prop.GetString() is { } id)
            {
                return id;
            }
        }
        catch (JsonException)
        {
        }

        return Guid.NewGuid().ToString("D");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _cts?.Dispose();
        if (_ownsStore && Store is IDisposable disposableStore)
        {
            disposableStore.Dispose();
        }
    }
}

