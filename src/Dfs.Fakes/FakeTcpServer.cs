using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Dfs.Shared.Messages;

namespace Dfs.Fakes;

/// <summary>Loopback-only TCP host for fake protocol responses.</summary>
public abstract class FakeTcpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private Task? _acceptTask;

    /// <summary>Address usable by another process on the same machine after Start.</summary>
    public string Address { get; private set; } = "";

    /// <summary>Starts the fake server on an available loopback port.</summary>
    public void Start()
    {
        if (_acceptTask is not null) throw new InvalidOperationException("Already started.");
        _listener.Start();
        Address = $"127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
        _acceptTask = AcceptAsync();
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _ = ServeAsync(client);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            var greeted = false;
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var frame = await FakeWire.ReadAsync(stream, _stop.Token);
                    if (frame is null) break;
                    try
                    {
                        if (!greeted)
                        {
                            if (frame.Value.Type != MessageType.Hello)
                                throw new DfsException(ErrorCode.BadRequest, "Hello must be first.");
                            var hello = FakeWire.Parse<Hello>(frame.Value.Payload);
                            if (hello.ProtocolVersion != 1)
                                throw new DfsException(ErrorCode.VersionMismatch, "Expected protocol version 1.");
                            await FakeWire.WriteAsync(stream, MessageType.HelloResponse,
                                new HelloResponse(hello.RequestId, 1, NodeType, NodeId), _stop.Token);
                            greeted = true;
                            continue;
                        }

                        await HandleAsync(stream, frame.Value.Type, frame.Value.Payload, _stop.Token);
                    }
                    catch (DfsException error)
                    {
                        var requestId = RequestId(frame.Value.Payload);
                        await FakeWire.WriteAsync(stream, MessageType.Error,
                            new ErrorResponse(requestId, error.Code, error.Message, error.Details?.ToDictionary(pair => pair.Key, pair => pair.Value)), _stop.Token);
                        if (!greeted) break;
                    }
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (IOException) { }
            catch (JsonException) { }
        }
    }

    private static string RequestId(byte[] payload)
    {
        try
        {
            using var json = JsonDocument.Parse(payload);
            return json.RootElement.TryGetProperty("requestId", out var value) ? value.GetString() ?? "" : "";
        }
        catch (JsonException) { return ""; }
    }

    protected abstract NodeType NodeType { get; }
    protected abstract string NodeId { get; }
    protected abstract Task HandleAsync(Stream stream, MessageType type, byte[] payload, CancellationToken cancellationToken);

    /// <summary>Stops listening and closes current fake connections.</summary>
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        if (_acceptTask is not null) await _acceptTask;
        _stop.Dispose();
    }
}
