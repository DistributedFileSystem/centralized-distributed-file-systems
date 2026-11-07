using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dfs.Fakes;
using Dfs.Shared.Messages;

namespace Dfs.Tests.Fakes;

public class FakeTcpServersTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Metadata_location_points_to_a_readable_chunk_and_uploads_can_be_committed()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var data = new FakeDataServer();
        data.Start();
        await using var metadata = new FakeMetadataServer(data);
        metadata.Start();

        using var metadataSocket = await ConnectAsync(metadata.Address, timeout.Token);
        var metadataStream = metadataSocket.GetStream();
        await SendAsync(metadataStream, MessageType.Hello, new Hello("hello-m", 1, NodeType.Client), timeout.Token);
        Assert.Equal(MessageType.HelloResponse, (await ReceiveAsync(metadataStream, timeout.Token)).Type);

        await SendAsync(metadataStream, MessageType.Login, new Login("login", "demo", "demo"), timeout.Token);
        var login = Read<LoginResponse>(await ReceiveAsync(metadataStream, timeout.Token), MessageType.LoginResponse);
        await SendAsync(metadataStream, MessageType.ListDir, new ListDir("list", login.Session, "/projects/dfs"), timeout.Token);
        var directory = Read<ListDirResponse>(await ReceiveAsync(metadataStream, timeout.Token), MessageType.ListDirResponse);
        Assert.Equal(["readme.txt"], directory.Items.Select(item => item.Name));

        await SendAsync(metadataStream, MessageType.Open, new Open("open", login.Session, "/projects/dfs/readme.txt"), timeout.Token);
        var opened = Read<OpenResponse>(await ReceiveAsync(metadataStream, timeout.Token), MessageType.OpenResponse);
        var chunk = Assert.Single(opened.Chunks);
        Assert.Equal(data.Address, Assert.Single(chunk.Servers));

        using var dataSocket = await ConnectAsync(data.Address, timeout.Token);
        var dataStream = dataSocket.GetStream();
        await SendAsync(dataStream, MessageType.Hello, new Hello("hello-d", 1, NodeType.Client), timeout.Token);
        Assert.Equal(MessageType.HelloResponse, (await ReceiveAsync(dataStream, timeout.Token)).Type);
        await SendAsync(dataStream, MessageType.ReadChunk, new ReadChunk("read", chunk.ChunkId, chunk.Token), timeout.Token);
        var readFrame = await ReceiveAsync(dataStream, timeout.Token);
        Assert.Equal(MessageType.ReadChunkResponse, readFrame.Type);
        var (readHeader, readBytes) = ReadChunkPayload<ReadChunkResponse>(readFrame.Payload);
        Assert.Equal(readHeader.Sha256, Convert.ToHexStringLower(SHA256.HashData(readBytes)));
        Assert.Equal("Distributed file system demo\n", Encoding.UTF8.GetString(readBytes));

        var newBytes = Encoding.UTF8.GetBytes("new file content");
        await SendAsync(metadataStream, MessageType.CreateUpload,
            new CreateUpload("plan", login.Session, "/projects/dfs/new.txt", newBytes.Length, 0), timeout.Token);
        var plan = Read<CreateUploadResponse>(await ReceiveAsync(metadataStream, timeout.Token), MessageType.CreateUploadResponse);
        var target = Assert.Single(plan.Chunks);
        Assert.Equal(data.Address, Assert.Single(target.Servers));
        await SendChunkAsync(dataStream, new WriteChunk("write", target.ChunkId, target.Token, newBytes.Length), newBytes, timeout.Token);
        var written = Read<WriteChunkResponse>(await ReceiveAsync(dataStream, timeout.Token), MessageType.WriteChunkResponse);
        await SendAsync(metadataStream, MessageType.CommitUpload,
            new CommitUpload("commit", login.Session, plan.UploadId,
                new ValueList<ChecksumInfo>([new(target.ChunkId, written.Size, written.Sha256)])), timeout.Token);
        var committed = Read<CommitUploadResponse>(await ReceiveAsync(metadataStream, timeout.Token), MessageType.CommitUploadResponse);
        Assert.Equal(1, committed.Version);
        Assert.Equal("/projects/dfs/new.txt", committed.Item.Path);
        await SendAsync(metadataStream, MessageType.Open, new Open("open-new", login.Session, committed.Item.Path), timeout.Token);
        var uploaded = Read<OpenResponse>(await ReceiveAsync(metadataStream, timeout.Token), MessageType.OpenResponse);
        var uploadedChunk = Assert.Single(uploaded.Chunks);
        Assert.Equal(written.Sha256, uploadedChunk.Sha256);
        await SendAsync(dataStream, MessageType.ReadChunk,
            new ReadChunk("read-new", uploadedChunk.ChunkId, uploadedChunk.Token), timeout.Token);
        var uploadedFrame = await ReceiveAsync(dataStream, timeout.Token);
        Assert.Equal(MessageType.ReadChunkResponse, uploadedFrame.Type);
        var (_, uploadedBytes) = ReadChunkPayload<ReadChunkResponse>(uploadedFrame.Payload);
        Assert.Equal(newBytes, uploadedBytes);

        await SendAsync(metadataStream, MessageType.Logout, new Logout("logout", login.Session), timeout.Token);
        Assert.Equal(MessageType.LogoutResponse, (await ReceiveAsync(metadataStream, timeout.Token)).Type);
        await SendAsync(metadataStream, MessageType.ListDir, new ListDir("after-logout", login.Session, "/"), timeout.Token);
        Assert.Equal(ErrorCode.Unauthorized,
            Read<ErrorResponse>(await ReceiveAsync(metadataStream, timeout.Token), MessageType.Error).Code);
    }

    private static async Task<TcpClient> ConnectAsync(string address, CancellationToken token)
    {
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, int.Parse(address.Split(':')[1]), token);
        return client;
    }

    private static async Task SendAsync(Stream stream, MessageType type, object value, CancellationToken token)
        => await SendFrameAsync(stream, type, JsonSerializer.SerializeToUtf8Bytes(value, Json), token);

    private static async Task SendChunkAsync(Stream stream, WriteChunk header, byte[] bytes, CancellationToken token)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(header, Json);
        var payload = new byte[4 + json.Length + bytes.Length];
        BinaryPrimitives.WriteInt32BigEndian(payload, json.Length);
        json.CopyTo(payload, 4);
        bytes.CopyTo(payload, 4 + json.Length);
        await SendFrameAsync(stream, MessageType.WriteChunk, payload, token);
    }

    private static async Task SendFrameAsync(Stream stream, MessageType type, byte[] payload, CancellationToken token)
    {
        var header = new byte[5];
        BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
        header[4] = (byte)type;
        await stream.WriteAsync(header, token);
        await stream.WriteAsync(payload, token);
    }

    private static async Task<(MessageType Type, byte[] Payload)> ReceiveAsync(Stream stream, CancellationToken token)
    {
        var header = new byte[5];
        await stream.ReadExactlyAsync(header, token);
        var payload = new byte[BinaryPrimitives.ReadInt32BigEndian(header)];
        await stream.ReadExactlyAsync(payload, token);
        return ((MessageType)header[4], payload);
    }

    private static T Read<T>((MessageType Type, byte[] Payload) frame, MessageType expected)
    {
        Assert.Equal(expected, frame.Type);
        return JsonSerializer.Deserialize<T>(frame.Payload, Json)!;
    }

    private static (T Header, byte[] Bytes) ReadChunkPayload<T>(byte[] payload)
    {
        var headerSize = BinaryPrimitives.ReadInt32BigEndian(payload);
        return (JsonSerializer.Deserialize<T>(payload.AsSpan(4, headerSize), Json)!, payload[(4 + headerSize)..]);
    }
}
