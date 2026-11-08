using Dfs.Shared.Messages;

namespace Dfs.Shared.Framing;

/// <summary>
/// Performs client and server handshake operations according to docs/protocol.md:
/// The first message of every connection must be Hello; server answers with HelloResponse
/// or Error(VersionMismatch) or Error(BadRequest).
/// </summary>
public static class HandshakeHandler
{
    /// <summary>
    /// Executes the client-side handshake: sends Hello and awaits HelloResponse.
    /// </summary>
    /// <param name="stream">The connected network stream.</param>
    /// <param name="hello">The Hello request to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The HelloResponse from the server.</returns>
    /// <exception cref="DfsException">
    /// Thrown if the server returns Error(VersionMismatch), Error(BadRequest), or another error,
    /// or if the response message type is unexpected.
    /// </exception>
    public static async Task<HelloResponse> ClientHandshakeAsync(
        Stream stream,
        Hello hello,
        CancellationToken cancellationToken = default)
    {
        await FrameCodec.WriteMessageAsync(stream, MessageType.Hello, hello, cancellationToken).ConfigureAwait(false);

        var frame = await FrameCodec.ReadRawFrameAsync(stream, cancellationToken).ConfigureAwait(false)
            ?? throw new DfsException(ErrorCode.Internal, "Connection closed by remote host during handshake.");

        if (frame.Type == MessageType.Error)
        {
            var error = FrameSerialization.DeserializeJson<ErrorResponse>(frame.Payload);
            throw new DfsException(error.Code, error.Message, error.Details);
        }

        if (frame.Type != MessageType.HelloResponse)
        {
            throw new DfsException(
                ErrorCode.BadRequest,
                $"Expected HelloResponse (0x{(byte)MessageType.HelloResponse:X2}) but received {frame.Type} (0x{(byte)frame.Type:X2}).");
        }

        return FrameSerialization.DeserializeJson<HelloResponse>(frame.Payload);
    }

    /// <summary>
    /// Executes the server-side handshake: validates that the first frame is Hello with protocol version 1,
    /// and replies with HelloResponse or appropriate Error response before completing.
    /// </summary>
    /// <param name="stream">The incoming network stream.</param>
    /// <param name="serverNodeId">The node identifier of this server.</param>
    /// <param name="serverNodeType">The node kind of this server (Metadata or Data).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The received Hello request, or <c>null</c> if the stream closed cleanly before any byte was sent.</returns>
    /// <exception cref="DfsException">
    /// Thrown with ErrorCode.BadRequest if a non-Hello message was sent or message was malformed,
    /// or with ErrorCode.VersionMismatch if the protocol version does not match.
    /// In all failure cases, an ErrorResponse frame is written to the stream before throwing.
    /// </exception>
    public static async Task<Hello?> ServerHandshakeAsync(
        Stream stream,
        string serverNodeId,
        NodeType serverNodeType,
        CancellationToken cancellationToken = default)
    {
        var frame = await FrameCodec.ReadRawFrameAsync(stream, cancellationToken).ConfigureAwait(false);
        if (frame is null)
        {
            return null;
        }

        if (frame.Type != MessageType.Hello)
        {
            var error = new ErrorResponse(
                Guid.NewGuid().ToString("D"),
                ErrorCode.BadRequest,
                $"First message must be Hello (0x{(byte)MessageType.Hello:X2}), but received {frame.Type} (0x{(byte)frame.Type:X2}).");

            await FrameCodec.WriteMessageAsync(stream, MessageType.Error, error, cancellationToken).ConfigureAwait(false);
            throw new DfsException(ErrorCode.BadRequest, error.Message);
        }

        Hello hello;
        try
        {
            hello = FrameSerialization.DeserializeJson<Hello>(frame.Payload);
        }
        catch (DfsException ex)
        {
            var error = new ErrorResponse(
                Guid.NewGuid().ToString("D"),
                ErrorCode.BadRequest,
                "Malformed Hello message: " + ex.Message);

            await FrameCodec.WriteMessageAsync(stream, MessageType.Error, error, cancellationToken).ConfigureAwait(false);
            throw;
        }

        if (hello.ProtocolVersion != ProtocolConstants.CurrentProtocolVersion)
        {
            var error = new ErrorResponse(
                hello.RequestId,
                ErrorCode.VersionMismatch,
                $"Protocol version {hello.ProtocolVersion} is not supported. Supported version is {ProtocolConstants.CurrentProtocolVersion}.");

            await FrameCodec.WriteMessageAsync(stream, MessageType.Error, error, cancellationToken).ConfigureAwait(false);
            throw new DfsException(ErrorCode.VersionMismatch, error.Message);
        }

        var response = new HelloResponse(
            hello.RequestId,
            ProtocolConstants.CurrentProtocolVersion,
            serverNodeType,
            serverNodeId);

        await FrameCodec.WriteMessageAsync(stream, MessageType.HelloResponse, response, cancellationToken).ConfigureAwait(false);
        return hello;
    }
}
