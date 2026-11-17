using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Dfs.DataServer.Config;
using Dfs.DataServer.Storage;
using Dfs.Shared.Framing;
using Dfs.Shared.Messages;
using Dfs.Shared.Security;

namespace Dfs.DataServer.Hosting;

/// <summary>
/// TCP server host for the Data Server that listens on LISTEN_PORT,
/// handles client handshakes, validates data access tokens, and processes chunk read, write, and delete operations.
/// </summary>
public sealed class DataServerHost : IAsyncDisposable
{
    private readonly bool _ownsStorage;
    private readonly IChunkStorage _storage;
    private readonly DataTokenVerifier _tokenVerifier;
    private readonly ConcurrentDictionary<Task, bool> _activeConnections = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoopTask;

    /// <summary>Configuration used by this Data Server.</summary>
    public DataServerConfig Config { get; }

    /// <summary>Actual bound TCP port.</summary>
    public int BoundPort { get; private set; }

    /// <summary>True if server is currently listening.</summary>
    public bool IsRunning => _listener is not null;

    /// <summary>
    /// Initializes a new instance of <see cref="DataServerHost"/>.
    /// </summary>
    /// <param name="config">Configuration for the server.</param>
    /// <param name="storage">Optional chunk storage; creates a default ChunkStorage if null.</param>
    /// <param name="timeProvider">Optional time provider for token verification.</param>
    public DataServerHost(DataServerConfig config, IChunkStorage? storage = null, TimeProvider? timeProvider = null)
    {
        Config = config;
        if (storage is not null)
        {
            _storage = storage;
            _ownsStorage = false;
        }
        else
        {
            _storage = new ChunkStorage(config.DataDir, config.CapacityBytes);
            _ownsStorage = true;
        }

        _tokenVerifier = new DataTokenVerifier(config.TokenKey, timeProvider);
    }

    /// <summary>
    /// Starts the TCP listener on the configured port.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_listener is not null)
        {
            throw new InvalidOperationException("DataServerHost is already running.");
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
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await StartAsync(cancellationToken).ConfigureAwait(false);
        await WaitForShutdownAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Awaits server shutdown.
    /// </summary>
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
                _ = connectionTask.ContinueWith(
                    t => _activeConnections.TryRemove(t, out _),
                    TaskScheduler.Default);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                if (cancellationToken.IsCancellationRequested) break;
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
                    NodeType.Data,
                    cancellationToken).ConfigureAwait(false);

                if (hello is null)
                {
                    return;
                }

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
                // Handshake error response already sent by HandshakeHandler
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
            }
            catch (SocketException)
            {
            }
        }
    }

    private async Task DispatchFrameAsync(Stream stream, RawFrame frame, CancellationToken cancellationToken)
    {
        string? currentRequestId = null;
        try
        {
            switch (frame.Type)
            {
                case MessageType.WriteChunk:
                {
                    var chunkFrame = FrameCodec.DecodeChunkPayload<WriteChunk>(frame.Payload);
                    var header = chunkFrame.Header;
                    currentRequestId = header.RequestId;

                    if (string.IsNullOrWhiteSpace(header.RequestId))
                        throw new DfsException(ErrorCode.BadRequest, "RequestId must not be empty.");
                    if (string.IsNullOrWhiteSpace(header.ChunkId))
                        throw new DfsException(ErrorCode.BadRequest, "ChunkId must not be empty.");
                    if (header.Size < 0)
                        throw new DfsException(ErrorCode.BadRequest, "Size cannot be negative.");
                    if (header.Size != chunkFrame.ChunkBytes.Length)
                        throw new DfsException(ErrorCode.BadRequest, $"Payload size ({chunkFrame.ChunkBytes.Length}) does not match declared size ({header.Size}).");

                    _tokenVerifier.Verify(header.Token, header.ChunkId, "w");

                    var result = await _storage.WriteChunkAsync(header.ChunkId, chunkFrame.ChunkBytes, cancellationToken).ConfigureAwait(false);
                    var response = new WriteChunkResponse(header.RequestId, header.ChunkId, result.Size, result.Sha256);
                    await FrameCodec.WriteMessageAsync(stream, MessageType.WriteChunkResponse, response, cancellationToken).ConfigureAwait(false);
                    return;
                }

                case MessageType.ReadChunk:
                {
                    var request = FrameSerialization.DeserializeJson<ReadChunk>(frame.Payload);
                    currentRequestId = request.RequestId;
                    if (string.IsNullOrWhiteSpace(request.RequestId))
                        throw new DfsException(ErrorCode.BadRequest, "RequestId must not be empty.");
                    if (string.IsNullOrWhiteSpace(request.ChunkId))
                        throw new DfsException(ErrorCode.BadRequest, "ChunkId must not be empty.");

                    _tokenVerifier.Verify(request.Token, request.ChunkId, "r");

                    var result = await _storage.ReadChunkAsync(request.ChunkId, cancellationToken).ConfigureAwait(false);
                    var responseHeader = new ReadChunkResponse(request.RequestId, request.ChunkId, result.Size, result.Sha256);
                    await FrameCodec.WriteChunkFrameAsync(stream, MessageType.ReadChunkResponse, responseHeader, result.Data, cancellationToken).ConfigureAwait(false);
                    return;
                }

                case MessageType.DeleteChunk:
                {
                    var request = FrameSerialization.DeserializeJson<DeleteChunk>(frame.Payload);
                    currentRequestId = request.RequestId;
                    if (string.IsNullOrWhiteSpace(request.RequestId))
                        throw new DfsException(ErrorCode.BadRequest, "RequestId must not be empty.");
                    if (request.ChunkIds is null)
                        throw new DfsException(ErrorCode.BadRequest, "ChunkIds must not be null.");

                    var deleted = await _storage.DeleteChunksAsync(request.ChunkIds, cancellationToken).ConfigureAwait(false);
                    var response = new DeleteChunkResponse(request.RequestId, deleted);
                    await FrameCodec.WriteMessageAsync(stream, MessageType.DeleteChunkResponse, response, cancellationToken).ConfigureAwait(false);
                    return;
                }

                default:
                    throw new DfsException(ErrorCode.BadRequest, $"Unsupported data server message {frame.Type} (0x{(byte)frame.Type:X2}).");
            }
        }
        catch (DfsException ex)
        {
            var reqId = !string.IsNullOrWhiteSpace(currentRequestId) ? currentRequestId : ExtractRequestId(frame);
            await WriteErrorAsync(stream, reqId, ex.Code, ex.Message, ex.Details, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            var reqId = !string.IsNullOrWhiteSpace(currentRequestId) ? currentRequestId : ExtractRequestId(frame);
            await WriteErrorAsync(stream, reqId, ErrorCode.Internal, "Internal server error.", null, cancellationToken).ConfigureAwait(false);
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

    private static string ExtractRequestId(RawFrame frame)
    {
        var payload = frame.Payload.AsSpan();
        if (frame.Type == MessageType.WriteChunk)
        {
            if (payload.Length < 4)
            {
                return Guid.NewGuid().ToString("D");
            }
            var headerLength = BinaryPrimitives.ReadInt32BigEndian(payload[..4]);
            if (headerLength <= 0 || payload.Length < 4 + headerLength)
            {
                return Guid.NewGuid().ToString("D");
            }
            payload = payload.Slice(4, headerLength);
        }

        try
        {
            var reader = new Utf8JsonReader(payload);
            using var doc = JsonDocument.ParseValue(ref reader);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                (doc.RootElement.TryGetProperty("requestId", out var prop) ||
                 doc.RootElement.TryGetProperty("RequestId", out prop)) &&
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
        if (_ownsStorage && _storage is IDisposable disposableStorage)
        {
            disposableStorage.Dispose();
        }
    }
}
