using System.Net.Sockets;
using Dfs.DataServer.Config;
using Dfs.DataServer.Storage;
using Dfs.Shared.Framing;
using Dfs.Shared.Messages;

namespace Dfs.DataServer.Hosting;

/// <summary>
/// Background client running on the Data Server that connects to the Metadata Server,
/// performs registration using the advertise address, and periodically emits heartbeats every 3 seconds.
/// </summary>
public sealed class HeartbeatClient : IAsyncDisposable
{
    private readonly DataServerConfig _config;
    private readonly IChunkStorage _storage;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _reconnectDelay;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    /// <summary>True if the Data Server is currently connected and registered with Metadata Server.</summary>
    public bool IsRegistered { get; private set; }

    /// <summary>Current active Metadata Server endpoint, or null if disconnected.</summary>
    public string? CurrentEndpoint { get; private set; }

    /// <summary>
    /// Initializes a new instance of <see cref="HeartbeatClient"/>.
    /// </summary>
    public HeartbeatClient(
        DataServerConfig config,
        IChunkStorage storage,
        TimeProvider? timeProvider = null,
        TimeSpan? reconnectDelay = null)
    {
        _config = config;
        _storage = storage;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _reconnectDelay = reconnectDelay ?? TimeSpan.FromSeconds(1);
    }

    /// <summary>
    /// Starts the registration and periodic heartbeat loop.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_loopTask is not null)
        {
            throw new InvalidOperationException("HeartbeatClient is already running.");
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loopTask = Task.Run(() => RunLoopAsync(_cts.Token), CancellationToken.None);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops the heartbeat loop and disconnects.
    /// </summary>
    public async Task StopAsync()
    {
        if (_cts is not null && !_cts.IsCancellationRequested)
        {
            _cts.Cancel();
        }

        if (_loopTask is not null)
        {
            try
            {
                await _loopTask.ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }

        IsRegistered = false;
        CurrentEndpoint = null;
        _loopTask = null;
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            foreach (var endpoint in _config.MetadataEndpoints)
            {
                if (cancellationToken.IsCancellationRequested) break;

                try
                {
                    await RunSessionAsync(endpoint, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception)
                {
                    // Failover to next metadata endpoint
                }
            }

            IsRegistered = false;
            CurrentEndpoint = null;

            if (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_reconnectDelay, _timeProvider, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task RunSessionAsync(string endpoint, CancellationToken cancellationToken)
    {
        var (host, port) = ParseEndpoint(endpoint);

        using var client = new TcpClient();
        await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
        await using var stream = client.GetStream();

        // 1. Hello handshake
        var hello = new Hello(
            Guid.NewGuid().ToString("D"),
            ProtocolConstants.CurrentProtocolVersion,
            NodeType.Data);

        var helloResponse = await HandshakeHandler.ClientHandshakeAsync(stream, hello, cancellationToken).ConfigureAwait(false);
        if (helloResponse.ProtocolVersion != ProtocolConstants.CurrentProtocolVersion)
        {
            throw new DfsException(ErrorCode.VersionMismatch, "Protocol version mismatch during data handshake.");
        }

        CurrentEndpoint = endpoint;

        // 2. Register
        var capacity = await _storage.GetCapacityBytesAsync(cancellationToken).ConfigureAwait(false);
        var free = await _storage.GetFreeBytesAsync(cancellationToken).ConfigureAwait(false);

        var register = new Register(
            Guid.NewGuid().ToString("D"),
            _config.NodeId,
            _config.AdvertiseAddress,
            capacity,
            free);

        await FrameCodec.WriteMessageAsync(stream, MessageType.Register, register, cancellationToken).ConfigureAwait(false);
        var registerResponse = await FrameCodec.ReadMessageAsync<RegisterResponse>(stream, MessageType.RegisterResponse, cancellationToken).ConfigureAwait(false)
            ?? throw new DfsException(ErrorCode.Internal, "Connection closed while awaiting RegisterResponse.");

        IsRegistered = true;

        var intervalSeconds = registerResponse.HeartbeatIntervalSeconds > 0
            ? registerResponse.HeartbeatIntervalSeconds
            : 3;
        var interval = TimeSpan.FromSeconds(intervalSeconds);

        // 3. Periodic heartbeat
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(interval, _timeProvider, cancellationToken).ConfigureAwait(false);

            var currentFree = await _storage.GetFreeBytesAsync(cancellationToken).ConfigureAwait(false);
            var chunkCount = await _storage.GetChunkCountAsync(cancellationToken).ConfigureAwait(false);

            var heartbeat = new Heartbeat(
                Guid.NewGuid().ToString("D"),
                _config.NodeId,
                currentFree,
                chunkCount);

            try
            {
                await FrameCodec.WriteMessageAsync(stream, MessageType.Heartbeat, heartbeat, cancellationToken).ConfigureAwait(false);
                var heartbeatResponse = await FrameCodec.ReadMessageAsync<HeartbeatResponse>(stream, MessageType.HeartbeatResponse, cancellationToken).ConfigureAwait(false);
                if (heartbeatResponse is null)
                {
                    throw new DfsException(ErrorCode.Unavailable, "Connection closed while awaiting HeartbeatResponse.");
                }
            }
            catch (DfsException ex) when (ex.Code == ErrorCode.NotFound)
            {
                // Metadata Server forgot our registration (e.g. Restarted); re-register
                IsRegistered = false;
                register = new Register(
                    Guid.NewGuid().ToString("D"),
                    _config.NodeId,
                    _config.AdvertiseAddress,
                    await _storage.GetCapacityBytesAsync(cancellationToken).ConfigureAwait(false),
                    await _storage.GetFreeBytesAsync(cancellationToken).ConfigureAwait(false));

                await FrameCodec.WriteMessageAsync(stream, MessageType.Register, register, cancellationToken).ConfigureAwait(false);
                await FrameCodec.ReadMessageAsync<RegisterResponse>(stream, MessageType.RegisterResponse, cancellationToken).ConfigureAwait(false);
                IsRegistered = true;
            }
        }
    }

    private static (string Host, int Port) ParseEndpoint(string endpoint)
    {
        var colonIndex = endpoint.LastIndexOf(':');
        if (colonIndex <= 0 || colonIndex == endpoint.Length - 1)
        {
            throw new ArgumentException($"Invalid endpoint format: {endpoint}", nameof(endpoint));
        }

        var host = endpoint[..colonIndex].TrimStart('[').TrimEnd(']');
        var portStr = endpoint[(colonIndex + 1)..];
        if (!int.TryParse(portStr, out var port) || port is < 1 or > 65535)
        {
            throw new ArgumentException($"Invalid port in endpoint: {endpoint}", nameof(endpoint));
        }

        return (host, port);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _cts?.Dispose();
    }
}
