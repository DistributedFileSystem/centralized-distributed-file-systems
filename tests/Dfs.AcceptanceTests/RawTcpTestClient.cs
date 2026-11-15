using System.Net;
using System.Net.Sockets;
using Dfs.Shared.Framing;
using Dfs.Shared.Messages;

namespace Dfs.AcceptanceTests;

/// <summary>
/// Raw-TCP test client for black-box testing against DFS servers over the wire protocol.
/// Communicates over TCP using framing and serialization defined in Dfs.Shared.
/// </summary>
public sealed class RawTcpTestClient : IAsyncDisposable, IDisposable
{
    private readonly TcpClient _tcpClient;
    private NetworkStream? _stream;
    private bool _disposed;

    public RawTcpTestClient()
    {
        _tcpClient = new TcpClient();
    }

    public RawTcpTestClient(TcpClient tcpClient)
    {
        _tcpClient = tcpClient ?? throw new ArgumentNullException(nameof(tcpClient));
        if (tcpClient.Connected)
        {
            _stream = tcpClient.GetStream();
        }
    }

    /// <summary>
    /// Connects to a target host and port via TCP.
    /// </summary>
    public static async Task<RawTcpTestClient> ConnectAsync(
        string host,
        int port,
        CancellationToken cancellationToken = default)
    {
        var client = new RawTcpTestClient();
        await client.ConnectInternalAsync(host, port, cancellationToken).ConfigureAwait(false);
        return client;
    }

    /// <summary>
    /// Connects to an endpoint string (e.g. "127.0.0.1:7000").
    /// </summary>
    public static async Task<RawTcpTestClient> ConnectAsync(
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ArgumentException("Endpoint cannot be null or empty.", nameof(endpoint));
        }

        var parts = endpoint.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var port))
        {
            throw new ArgumentException($"Invalid endpoint format '{endpoint}'. Expected 'host:port'.", nameof(endpoint));
        }

        return await ConnectAsync(parts[0], port, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Connects to an IPEndPoint via TCP.
    /// </summary>
    public static async Task<RawTcpTestClient> ConnectAsync(
        IPEndPoint endPoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endPoint);
        var client = new RawTcpTestClient();
        await client._tcpClient.ConnectAsync(endPoint.Address, endPoint.Port, cancellationToken).ConfigureAwait(false);
        client._stream = client._tcpClient.GetStream();
        return client;
    }

    private async Task ConnectInternalAsync(string host, int port, CancellationToken cancellationToken)
    {
        await _tcpClient.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
        _stream = _tcpClient.GetStream();
    }

    public bool IsConnected => _tcpClient.Connected;

    public TcpClient TcpClient => _tcpClient;

    public NetworkStream Stream =>
        _stream ?? throw new InvalidOperationException("Client is not connected. Call ConnectAsync first.");

    public EndPoint? RemoteEndPoint => _tcpClient.Client.RemoteEndPoint;

    /// <summary>
    /// Performs client handshake by sending Hello and receiving HelloResponse.
    /// </summary>
    public async Task<HelloResponse> HandshakeAsync(
        string? requestId = null,
        int protocolVersion = 1,
        NodeType nodeType = NodeType.Client,
        CancellationToken cancellationToken = default)
    {
        var hello = new Hello(requestId ?? Guid.NewGuid().ToString("D"), protocolVersion, nodeType);
        return await HandshakeHandler.ClientHandshakeAsync(Stream, hello, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a typed JSON control message.
    /// </summary>
    public Task SendMessageAsync<T>(MessageType type, T message, CancellationToken cancellationToken = default)
        where T : class
    {
        return FrameCodec.WriteMessageAsync(Stream, type, message, cancellationToken);
    }

    /// <summary>
    /// Reads and deserializes a typed JSON control message.
    /// </summary>
    public Task<T?> ReceiveMessageAsync<T>(MessageType expectedType, CancellationToken cancellationToken = default)
        where T : class
    {
        return FrameCodec.ReadMessageAsync<T>(Stream, expectedType, cancellationToken);
    }

    /// <summary>
    /// Sends a raw frame with a message type and payload bytes.
    /// </summary>
    public Task SendRawFrameAsync(MessageType type, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        return FrameCodec.WriteRawFrameAsync(Stream, type, payload, cancellationToken);
    }

    /// <summary>
    /// Reads a raw frame from the TCP stream.
    /// </summary>
    public Task<RawFrame?> ReceiveRawFrameAsync(CancellationToken cancellationToken = default)
    {
        return FrameCodec.ReadRawFrameAsync(Stream, cancellationToken);
    }

    /// <summary>
    /// Sends arbitrary raw bytes directly to the TCP stream without framing.
    /// Useful for testing malformed frames, torn writes, or protocol violations.
    /// </summary>
    public async Task SendRawBytesAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
    {
        await Stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await Stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a chunk frame (header + chunk data).
    /// </summary>
    public Task SendChunkFrameAsync<THeader>(
        MessageType type,
        THeader header,
        ReadOnlyMemory<byte> chunkBytes,
        CancellationToken cancellationToken = default)
        where THeader : class
    {
        return FrameCodec.WriteChunkFrameAsync(Stream, type, header, chunkBytes, cancellationToken);
    }

    /// <summary>
    /// Reads a chunk frame (header + chunk data).
    /// </summary>
    public Task<ChunkFrame<THeader>?> ReceiveChunkFrameAsync<THeader>(
        MessageType expectedType,
        CancellationToken cancellationToken = default)
        where THeader : class
    {
        return FrameCodec.ReadChunkFrameAsync<THeader>(Stream, expectedType, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stream?.Dispose();
        _tcpClient.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_stream is not null)
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }

        _tcpClient.Dispose();
    }
}
