using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Dfs.MetadataServer.Config;
using Dfs.MetadataServer.Store;
using Dfs.Shared.Framing;
using Dfs.Shared.Messages;
using Dfs.Shared.Metadata;

namespace Dfs.MetadataServer.Hosting;

/// <summary>
/// TCP server host for the Metadata Server that listens on LISTEN_PORT,
/// executes the Hello handshake on incoming connections, and maintains the metadata store.
/// </summary>
public sealed class MetadataServerHost : IAsyncDisposable
{
    private readonly bool _ownsStore;
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
    public MetadataServerHost(MetadataServerConfig config, IMetadataStore? store = null)
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

                // Stage 1 skeleton: connection handshake succeeded.
                // Keep reading frames until the client closes the connection or cancellation is triggered.
                while (!cancellationToken.IsCancellationRequested)
                {
                    var frame = await FrameCodec.ReadRawFrameAsync(stream, cancellationToken).ConfigureAwait(false);
                    if (frame is null)
                    {
                        break;
                    }

                    // In milestone M2, operation message handlers (auth, namespace, upload, cluster)
                    // will dispatch from this frame read loop.
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

