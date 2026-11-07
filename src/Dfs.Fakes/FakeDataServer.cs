using System.Collections.Concurrent;
using System.Security.Cryptography;
using Dfs.Shared.Messages;

namespace Dfs.Fakes;

/// <summary>Fake Data Server: chunk bytes live only in memory; TCP requests use the stage 1 framing.</summary>
public sealed class FakeDataServer : FakeTcpServer
{
    private readonly ConcurrentDictionary<string, byte[]> _chunks = new(StringComparer.Ordinal);

    public FakeDataServer() => Store(FakeSamples.ChunkId, FakeSamples.Content);

    protected override NodeType NodeType => Dfs.Shared.Messages.NodeType.Data;
    protected override string NodeId => "d-demo";

    /// <summary>Stores bytes under a chunk id and returns their checksum.</summary>
    public ChunkInfo Store(string chunkId, ReadOnlySpan<byte> bytes)
    {
        var copy = bytes.ToArray();
        var stored = _chunks.GetOrAdd(chunkId, copy);
        if (!stored.AsSpan().SequenceEqual(copy))
            throw new DfsException(ErrorCode.Conflict, "A different chunk already has this id.");
        return new ChunkInfo(chunkId, copy.Length, Convert.ToHexStringLower(SHA256.HashData(copy)));
    }

    /// <summary>Reads a copy of the chunk so callers cannot mutate stored bytes.</summary>
    public byte[] Read(string chunkId) => _chunks.TryGetValue(chunkId, out var bytes)
        ? bytes.ToArray() : throw new DfsException(ErrorCode.NotFound, "Chunk not found.");

    protected override async Task HandleAsync(Stream stream, MessageType type, byte[] payload, CancellationToken cancellationToken)
    {
        switch (type)
        {
            case MessageType.WriteChunk:
            {
                var (header, bytes) = FakeWire.ParseChunk<WriteChunk>(payload);
                FakeToken.Verify(header.Token, header.ChunkId, "w");
                if (header.Size != bytes.Length) throw new DfsException(ErrorCode.BadRequest, "Chunk size does not match bytes.");
                var chunk = Store(header.ChunkId, bytes);
                await FakeWire.WriteAsync(stream, MessageType.WriteChunkResponse,
                    new WriteChunkResponse(header.RequestId, chunk.ChunkId, chunk.Size, chunk.Sha256), cancellationToken);
                break;
            }
            case MessageType.ReadChunk:
            {
                var request = FakeWire.Parse<ReadChunk>(payload);
                FakeToken.Verify(request.Token, request.ChunkId, "r");
                var bytes = Read(request.ChunkId);
                var response = new ReadChunkResponse(request.RequestId, request.ChunkId, bytes.Length,
                    Convert.ToHexStringLower(SHA256.HashData(bytes)));
                await FakeWire.WriteChunkAsync(stream, response, bytes, cancellationToken);
                break;
            }
            case MessageType.DeleteChunk:
            {
                var request = FakeWire.Parse<DeleteChunk>(payload);
                var deleted = request.ChunkIds.Count(id => _chunks.TryRemove(id, out _));
                await FakeWire.WriteAsync(stream, MessageType.DeleteChunkResponse,
                    new DeleteChunkResponse(request.RequestId, deleted), cancellationToken);
                break;
            }
            default:
                throw new DfsException(ErrorCode.BadRequest, "Unsupported fake data request.");
        }
    }
}
