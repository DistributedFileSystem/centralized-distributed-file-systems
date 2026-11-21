using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dfs.Client.Core;
using Dfs.Shared.Framing;
using Dfs.Shared.Messages;
using Xunit;

namespace Dfs.AcceptanceTests;

/// <summary>
/// Black-box acceptance tests for Task M2-15 (Issue #33) — Phase 2:
/// End-to-end Upload → CommitUpload → Download → SHA-256 Verification.
/// Runs against a strictly isolated cluster (1 Metadata Server + 2 Data Servers).
/// </summary>
[Collection("IsolatedCluster")]
public class DataTransferAcceptanceTests
{
    private readonly IsolatedClusterFixture _fixture;

    public DataTransferAcceptanceTests(IsolatedClusterFixture fixture)
    {
        _fixture = fixture;
    }

    #region Helper Methods

    private async Task<int> GetActualChunkSizeAsync()
    {
        await using var client = await _fixture.CreateAdminClientAsync();
        await using var raw = await RawTcpTestClient.ConnectAsync(_fixture.Host, _fixture.MetadataPort);
        await raw.HandshakeAsync();

        var probePath = $"/probe-{Guid.NewGuid():N}.tmp";
        var createReq = new CreateUpload(Guid.NewGuid().ToString("D"), client.Session!, probePath, 1, 0);
        await raw.SendMessageAsync(MessageType.CreateUpload, createReq);

        var createRes = await raw.ReceiveMessageAsync<CreateUploadResponse>(MessageType.CreateUploadResponse);
        Assert.NotNull(createRes);
        Assert.True(createRes.ChunkSize > 0, "ChunkSize from CreateUpload must be greater than 0.");

        var abortReq = new AbortUpload(Guid.NewGuid().ToString("D"), client.Session!, createRes.UploadId);
        await raw.SendMessageAsync(MessageType.AbortUpload, abortReq);
        await raw.ReceiveMessageAsync<AbortUploadResponse>(MessageType.AbortUploadResponse);

        return createRes.ChunkSize;
    }

    private static byte[] GenerateDeterministicBinaryPayload(int length)
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
        {
            bytes[i] = (byte)((i * 31 + 7) & 0xFF);
        }

        // Explicitly inject nulls and 0xFF at boundaries and intermediate offsets
        if (length > 0) bytes[0] = 0x00;
        if (length > 1) bytes[1] = 0xFF;
        if (length > 2) bytes[length - 1] = 0x00;
        if (length > 3) bytes[length - 2] = 0xFF;
        if (length > 10)
        {
            bytes[length / 2] = 0x00;
            bytes[(length / 2) + 1] = 0xFF;
        }

        return bytes;
    }

    private static string ComputeSha256Hex(ReadOnlySpan<byte> data) =>
        Convert.ToHexStringLower(SHA256.HashData(data));

    private static string SignDataToken(string tokenKey, string userId, string chunkId, string operation)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds();
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            u = userId,
            c = chunkId,
            op = operation,
            exp = expiresAt
        });

        var keyBytes = Encoding.UTF8.GetBytes(tokenKey);
        var signatureBytes = HMACSHA256.HashData(keyBytes, payloadBytes);

        static string Base64Url(byte[] b) =>
            Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return $"{Base64Url(payloadBytes)}.{Base64Url(signatureBytes)}";
    }

    #endregion

    #region Category A: Basic Upload / Download & SHA-256 Verification

    [Fact]
    public async Task Upload_Download_SmallFile_MatchesByteForByteAndSha256()
    {
        await using var client = await _fixture.CreateAdminClientAsync();
        var filePath = $"/acc-small-{Guid.NewGuid():N}.txt";

        var originalText = "Hello DFS Distributed File System! Black-Box Acceptance Test " + Guid.NewGuid();
        var originalBytes = Encoding.UTF8.GetBytes(originalText);
        var expectedSha = ComputeSha256Hex(originalBytes);

        // 1. Upload
        using var uploadStream = new MemoryStream(originalBytes);
        var commitRes = await client.UploadFileAsync(filePath, uploadStream, baseVersion: 0);

        Assert.NotNull(commitRes);
        Assert.Equal(1, commitRes.Version);
        Assert.Equal(originalBytes.Length, commitRes.Item.Size);

        // 2. Download
        using var downloadStream = new MemoryStream();
        await client.DownloadFileAsync(filePath, downloadStream, version: 0);
        var downloadedBytes = downloadStream.ToArray();
        var actualSha = ComputeSha256Hex(downloadedBytes);

        // 3. Verify
        Assert.Equal(originalBytes.Length, downloadedBytes.Length);
        Assert.Equal(expectedSha, actualSha);
        Assert.Equal(originalBytes, downloadedBytes);
    }

    #endregion

    #region Category B: File Size Coverage

    [Fact]
    public async Task Upload_Download_EmptyFile_SucceedsWithZeroBytes()
    {
        await using var client = await _fixture.CreateAdminClientAsync();
        var filePath = $"/acc-empty-{Guid.NewGuid():N}.bin";

        var emptyBytes = Array.Empty<byte>();
        var expectedSha = ComputeSha256Hex(emptyBytes);

        // 1. Upload 0-byte file
        using var uploadStream = new MemoryStream(emptyBytes);
        var commitRes = await client.UploadFileAsync(filePath, uploadStream, baseVersion: 0);

        Assert.NotNull(commitRes);
        Assert.Equal(1, commitRes.Version);
        Assert.Equal(0, commitRes.Item.Size);

        // 2. Download
        using var downloadStream = new MemoryStream();
        await client.DownloadFileAsync(filePath, downloadStream);
        var downloadedBytes = downloadStream.ToArray();
        var actualSha = ComputeSha256Hex(downloadedBytes);

        // 3. Verify
        Assert.Empty(downloadedBytes);
        Assert.Equal(expectedSha, actualSha);
    }

    [Fact]
    public async Task Upload_Download_SubChunkFile_MatchesSha256()
    {
        var chunkSize = await GetActualChunkSizeAsync();
        var fileSize = Math.Min(chunkSize / 2, 64 * 1024); // smaller than 1 chunk

        await using var client = await _fixture.CreateAdminClientAsync();
        var filePath = $"/acc-subchunk-{Guid.NewGuid():N}.bin";

        var originalBytes = GenerateDeterministicBinaryPayload(fileSize);
        var expectedSha = ComputeSha256Hex(originalBytes);

        using var uploadStream = new MemoryStream(originalBytes);
        var commitRes = await client.UploadFileAsync(filePath, uploadStream, baseVersion: 0);
        Assert.Equal(1, commitRes.Version);

        using var downloadStream = new MemoryStream();
        await client.DownloadFileAsync(filePath, downloadStream);
        var downloadedBytes = downloadStream.ToArray();
        var actualSha = ComputeSha256Hex(downloadedBytes);

        Assert.Equal(originalBytes.Length, downloadedBytes.Length);
        Assert.Equal(expectedSha, actualSha);
        Assert.Equal(originalBytes, downloadedBytes);
    }

    [Fact]
    public async Task Upload_Download_ExactOneChunkFile_MatchesSha256()
    {
        var chunkSize = await GetActualChunkSizeAsync();
        var fileSize = chunkSize; // exactly one full chunk

        await using var client = await _fixture.CreateAdminClientAsync();
        var filePath = $"/acc-exact-{Guid.NewGuid():N}.bin";

        var originalBytes = GenerateDeterministicBinaryPayload(fileSize);
        var expectedSha = ComputeSha256Hex(originalBytes);

        using var uploadStream = new MemoryStream(originalBytes);
        var commitRes = await client.UploadFileAsync(filePath, uploadStream, baseVersion: 0);
        Assert.Equal(1, commitRes.Version);

        using var downloadStream = new MemoryStream();
        await client.DownloadFileAsync(filePath, downloadStream);
        var downloadedBytes = downloadStream.ToArray();
        var actualSha = ComputeSha256Hex(downloadedBytes);

        Assert.Equal(fileSize, downloadedBytes.Length);
        Assert.Equal(expectedSha, actualSha);
        Assert.Equal(originalBytes, downloadedBytes);
    }

    [Fact]
    public async Task Upload_Download_LargerThanOneChunkFile_MatchesSha256()
    {
        var chunkSize = await GetActualChunkSizeAsync();
        var fileSize = chunkSize + 1024; // spans across 2 chunks

        await using var client = await _fixture.CreateAdminClientAsync();
        var filePath = $"/acc-larger-{Guid.NewGuid():N}.bin";

        var originalBytes = GenerateDeterministicBinaryPayload(fileSize);
        var expectedSha = ComputeSha256Hex(originalBytes);

        using var uploadStream = new MemoryStream(originalBytes);
        var commitRes = await client.UploadFileAsync(filePath, uploadStream, baseVersion: 0);
        Assert.Equal(1, commitRes.Version);

        using var downloadStream = new MemoryStream();
        await client.DownloadFileAsync(filePath, downloadStream);
        var downloadedBytes = downloadStream.ToArray();
        var actualSha = ComputeSha256Hex(downloadedBytes);

        Assert.Equal(fileSize, downloadedBytes.Length);
        Assert.Equal(expectedSha, actualSha);
        Assert.Equal(originalBytes, downloadedBytes);
    }

    [Fact]
    public async Task Upload_Download_MultiChunkFile_MatchesSha256()
    {
        var chunkSize = await GetActualChunkSizeAsync();
        var fileSize = (2 * chunkSize) + 2048; // 3 chunks, distributed across data servers

        await using var client = await _fixture.CreateAdminClientAsync();
        var filePath = $"/acc-multi-{Guid.NewGuid():N}.bin";

        var originalBytes = GenerateDeterministicBinaryPayload(fileSize);
        var expectedSha = ComputeSha256Hex(originalBytes);

        using var uploadStream = new MemoryStream(originalBytes);
        var commitRes = await client.UploadFileAsync(filePath, uploadStream, baseVersion: 0);
        Assert.Equal(1, commitRes.Version);

        using var downloadStream = new MemoryStream();
        await client.DownloadFileAsync(filePath, downloadStream);
        var downloadedBytes = downloadStream.ToArray();
        var actualSha = ComputeSha256Hex(downloadedBytes);

        Assert.Equal(fileSize, downloadedBytes.Length);
        Assert.Equal(expectedSha, actualSha);
        Assert.Equal(originalBytes, downloadedBytes);
    }

    #endregion

    #region Category C: Binary Integrity

    [Fact]
    public async Task Upload_Download_BinaryPayload_WithNullsAndHighBytes_ByteForByteIntegrity()
    {
        await using var client = await _fixture.CreateAdminClientAsync();
        var filePath = $"/acc-binary-{Guid.NewGuid():N}.dat";

        // Construct 128 KB payload containing all 256 byte values, explicit zeroes, 0xFF, and control codes
        var originalBytes = new byte[128 * 1024];
        for (var i = 0; i < originalBytes.Length; i++)
        {
            originalBytes[i] = (byte)(i % 256);
        }

        // Deliberate patterns
        Array.Fill(originalBytes, (byte)0x00, 1000, 500); // 500 null bytes
        Array.Fill(originalBytes, (byte)0xFF, 5000, 500); // 500 0xFF bytes

        var expectedSha = ComputeSha256Hex(originalBytes);

        using var uploadStream = new MemoryStream(originalBytes);
        await client.UploadFileAsync(filePath, uploadStream, baseVersion: 0);

        using var downloadStream = new MemoryStream();
        await client.DownloadFileAsync(filePath, downloadStream);
        var downloadedBytes = downloadStream.ToArray();
        var actualSha = ComputeSha256Hex(downloadedBytes);

        Assert.Equal(originalBytes.Length, downloadedBytes.Length);
        Assert.Equal(expectedSha, actualSha);
        Assert.Equal(originalBytes, downloadedBytes);
    }

    #endregion

    #region Category D: Versioning & Concurrency

    [Fact]
    public async Task Upload_NewVersionWithBaseVersion_RetainsBothVersions()
    {
        await using var client = await _fixture.CreateAdminClientAsync();
        var filePath = $"/acc-versioned-{Guid.NewGuid():N}.txt";

        var v1Bytes = Encoding.UTF8.GetBytes("Version 1 content: " + Guid.NewGuid());
        var v1Sha = ComputeSha256Hex(v1Bytes);

        var v2Bytes = Encoding.UTF8.GetBytes("Version 2 content: " + Guid.NewGuid() + " with additional payload");
        var v2Sha = ComputeSha256Hex(v2Bytes);

        // Upload Version 1 (baseVersion = 0)
        using (var stream1 = new MemoryStream(v1Bytes))
        {
            var res1 = await client.UploadFileAsync(filePath, stream1, baseVersion: 0);
            Assert.Equal(1, res1.Version);
        }

        // Upload Version 2 (baseVersion = 1)
        using (var stream2 = new MemoryStream(v2Bytes))
        {
            var res2 = await client.UploadFileAsync(filePath, stream2, baseVersion: 1);
            Assert.Equal(2, res2.Version);
        }

        // Download Version 1 specifically
        using (var dl1 = new MemoryStream())
        {
            await client.DownloadFileAsync(filePath, dl1, version: 1);
            var dl1Bytes = dl1.ToArray();
            Assert.Equal(v1Bytes, dl1Bytes);
            Assert.Equal(v1Sha, ComputeSha256Hex(dl1Bytes));
        }

        // Download Version 2 specifically
        using (var dl2 = new MemoryStream())
        {
            await client.DownloadFileAsync(filePath, dl2, version: 2);
            var dl2Bytes = dl2.ToArray();
            Assert.Equal(v2Bytes, dl2Bytes);
            Assert.Equal(v2Sha, ComputeSha256Hex(dl2Bytes));
        }

        // Download current version (version = 0)
        using (var dlCurrent = new MemoryStream())
        {
            await client.DownloadFileAsync(filePath, dlCurrent, version: 0);
            var dlCurrentBytes = dlCurrent.ToArray();
            Assert.Equal(v2Bytes, dlCurrentBytes);
            Assert.Equal(v2Sha, ComputeSha256Hex(dlCurrentBytes));
        }
    }

    [Fact]
    public async Task Upload_StaleBaseVersion_ThrowsConflict()
    {
        await using var client = await _fixture.CreateAdminClientAsync();
        var filePath = $"/acc-stale-{Guid.NewGuid():N}.txt";

        var content1 = Encoding.UTF8.GetBytes("Initial file content");
        using (var stream1 = new MemoryStream(content1))
        {
            var res1 = await client.UploadFileAsync(filePath, stream1, baseVersion: 0);
            Assert.Equal(1, res1.Version);
        }

        // Try to upload with baseVersion = 0 on already existing file
        var content2 = Encoding.UTF8.GetBytes("Conflicting update");
        using (var stream2 = new MemoryStream(content2))
        {
            var ex = await Assert.ThrowsAsync<DfsException>(() =>
                client.UploadFileAsync(filePath, stream2, baseVersion: 0));
            Assert.Equal(ErrorCode.Conflict, ex.Code);
        }
    }

    #endregion

    #region Category E: Protocol-Level Error Handling & Checksum Distinction

    [Fact]
    public async Task Download_NonExistentFile_ThrowsNotFound()
    {
        await using var client = await _fixture.CreateAdminClientAsync();
        var nonExistentPath = $"/acc-missing-{Guid.NewGuid():N}.bin";

        using var ms = new MemoryStream();
        var ex = await Assert.ThrowsAsync<DfsException>(() =>
            client.DownloadFileAsync(nonExistentPath, ms));
        Assert.Equal(ErrorCode.NotFound, ex.Code);
    }

    [Fact]
    public async Task Download_NonExistentVersion_ThrowsNotFound()
    {
        await using var client = await _fixture.CreateAdminClientAsync();
        var filePath = $"/acc-verdoc-{Guid.NewGuid():N}.txt";

        var content = Encoding.UTF8.GetBytes("Doc content");
        using (var ms = new MemoryStream(content))
        {
            await client.UploadFileAsync(filePath, ms, baseVersion: 0);
        }

        using var dl = new MemoryStream();
        var ex = await Assert.ThrowsAsync<DfsException>(() =>
            client.DownloadFileAsync(filePath, dl, version: 999));
        Assert.Equal(ErrorCode.NotFound, ex.Code);
    }

    [Fact]
    public async Task AbortUpload_PreventsSubsequentCommit()
    {
        await using var client = await _fixture.CreateAdminClientAsync();
        await using var raw = await RawTcpTestClient.ConnectAsync(_fixture.Host, _fixture.MetadataPort);
        await raw.HandshakeAsync();

        var filePath = $"/acc-aborted-{Guid.NewGuid():N}.bin";

        // 1. CreateUpload
        var createReq = new CreateUpload(Guid.NewGuid().ToString("D"), client.Session!, filePath, 1024, 0);
        await raw.SendMessageAsync(MessageType.CreateUpload, createReq);
        var createRes = await raw.ReceiveMessageAsync<CreateUploadResponse>(MessageType.CreateUploadResponse);
        Assert.NotNull(createRes);

        // 2. AbortUpload
        var abortReq = new AbortUpload(Guid.NewGuid().ToString("D"), client.Session!, createRes.UploadId);
        await raw.SendMessageAsync(MessageType.AbortUpload, abortReq);
        var abortRes = await raw.ReceiveMessageAsync<AbortUploadResponse>(MessageType.AbortUploadResponse);
        Assert.NotNull(abortRes);

        // 3. CommitUpload should fail with NotFound because upload was aborted
        var fakeSha = ComputeSha256Hex(new byte[1024]);
        var chunk = createRes.Chunks[0];
        var checksums = new ValueList<ChecksumInfo>([new ChecksumInfo(chunk.ChunkId, 1024, fakeSha)]);

        var commitReq = new CommitUpload(Guid.NewGuid().ToString("D"), client.Session!, createRes.UploadId, checksums);
        await raw.SendMessageAsync(MessageType.CommitUpload, commitReq);

        var errFrame = await raw.ReceiveRawFrameAsync();
        Assert.NotNull(errFrame);
        Assert.Equal(MessageType.Error, errFrame.Type);
        var err = FrameSerialization.DeserializeJson<ErrorResponse>(errFrame.Payload);
        Assert.Equal(ErrorCode.NotFound, err.Code);
    }

    [Fact]
    public async Task CommitUpload_WithMalformedSha256_ReturnsBadRequest()
    {
        await using var client = await _fixture.CreateAdminClientAsync();
        await using var raw = await RawTcpTestClient.ConnectAsync(_fixture.Host, _fixture.MetadataPort);
        await raw.HandshakeAsync();

        var filePath = $"/acc-malformed-{Guid.NewGuid():N}.bin";

        // 1. CreateUpload
        var createReq = new CreateUpload(Guid.NewGuid().ToString("D"), client.Session!, filePath, 100, 0);
        await raw.SendMessageAsync(MessageType.CreateUpload, createReq);
        var createRes = await raw.ReceiveMessageAsync<CreateUploadResponse>(MessageType.CreateUploadResponse);
        Assert.NotNull(createRes);

        // 2. CommitUpload with an invalid malformed SHA-256 (not a valid 64 lowercase hex)
        var chunk = createRes.Chunks[0];
        var checksums = new ValueList<ChecksumInfo>([new ChecksumInfo(chunk.ChunkId, 100, "not-a-valid-sha256-string")]);

        var commitReq = new CommitUpload(Guid.NewGuid().ToString("D"), client.Session!, createRes.UploadId, checksums);
        await raw.SendMessageAsync(MessageType.CommitUpload, commitReq);

        var errFrame = await raw.ReceiveRawFrameAsync();
        Assert.NotNull(errFrame);
        Assert.Equal(MessageType.Error, errFrame.Type);
        var err = FrameSerialization.DeserializeJson<ErrorResponse>(errFrame.Payload);
        Assert.Equal(ErrorCode.BadRequest, err.Code);
    }

    [Fact]
    public async Task CommitUpload_WithMismatchedValidSha256_FailsDownloadVerification()
    {
        await using var client = await _fixture.CreateAdminClientAsync();
        await using var rawMeta = await RawTcpTestClient.ConnectAsync(_fixture.Host, _fixture.MetadataPort);
        await rawMeta.HandshakeAsync();

        var filePath = $"/acc-mismatched-{Guid.NewGuid():N}.bin";
        var payload = Encoding.UTF8.GetBytes("Real payload that is written to chunk storage");
        var realSha = ComputeSha256Hex(payload);

        // 1. CreateUpload
        var createReq = new CreateUpload(Guid.NewGuid().ToString("D"), client.Session!, filePath, payload.Length, 0);
        await rawMeta.SendMessageAsync(MessageType.CreateUpload, createReq);
        var createRes = await rawMeta.ReceiveMessageAsync<CreateUploadResponse>(MessageType.CreateUploadResponse);
        Assert.NotNull(createRes);
        var chunk = createRes.Chunks[0];

        // 2. Write actual chunk to Data Server
        var dsAddress = chunk.Servers[0];
        var parts = dsAddress.Split(':');
        await using var rawDs = await RawTcpTestClient.ConnectAsync(parts[0], int.Parse(parts[1]));
        await rawDs.HandshakeAsync(nodeType: NodeType.Client);

        var writeReq = new WriteChunk(Guid.NewGuid().ToString("D"), chunk.ChunkId, chunk.Token, payload.Length);
        await rawDs.SendChunkFrameAsync(MessageType.WriteChunk, writeReq, payload);
        var writeRes = await rawDs.ReceiveMessageAsync<WriteChunkResponse>(MessageType.WriteChunkResponse);
        Assert.NotNull(writeRes);
        Assert.Equal(realSha, writeRes.Sha256);

        // 3. CommitUpload with a well-formed 64-hex SHA-256 that does NOT match the chunk data
        // (Using SHA-256 of empty string: e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855)
        var mismatchedSha = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        var checksums = new ValueList<ChecksumInfo>([new ChecksumInfo(chunk.ChunkId, payload.Length, mismatchedSha)]);

        var commitReq = new CommitUpload(Guid.NewGuid().ToString("D"), client.Session!, createRes.UploadId, checksums);
        await rawMeta.SendMessageAsync(MessageType.CommitUpload, commitReq);
        var commitRes = await rawMeta.ReceiveMessageAsync<CommitUploadResponse>(MessageType.CommitUploadResponse);
        Assert.NotNull(commitRes);

        // 4. Download via DfsClient: should detect the mismatch between metadata SHA-256 and actual downloaded bytes
        using var dest = new MemoryStream();
        var ex = await Assert.ThrowsAsync<DfsException>(() =>
            client.DownloadFileAsync(filePath, dest));

        Assert.Equal(ErrorCode.Internal, ex.Code);
        Assert.Contains("checksum mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DataServer_WriteChunk_WithInvalidToken_ReturnsTokenInvalid()
    {
        await using var rawDs = await RawTcpTestClient.ConnectAsync(_fixture.Host, _fixture.DataPort1);
        await rawDs.HandshakeAsync(nodeType: NodeType.Client);

        var chunkId = $"c-{Guid.NewGuid():N}";
        var data = new byte[] { 1, 2, 3, 4 };
        var badToken = "invalid.tampered.token";

        var writeHeader = new WriteChunk(Guid.NewGuid().ToString("D"), chunkId, badToken, data.Length);
        await rawDs.SendChunkFrameAsync(MessageType.WriteChunk, writeHeader, data);

        var errFrame = await rawDs.ReceiveRawFrameAsync();
        Assert.NotNull(errFrame);
        Assert.Equal(MessageType.Error, errFrame.Type);
        var err = FrameSerialization.DeserializeJson<ErrorResponse>(errFrame.Payload);
        Assert.Equal(ErrorCode.TokenInvalid, err.Code);
    }

    [Fact]
    public async Task DataServer_ReadChunk_NonExistentChunk_ReturnsNotFound()
    {
        await using var rawDs = await RawTcpTestClient.ConnectAsync(_fixture.Host, _fixture.DataPort1);
        await rawDs.HandshakeAsync(nodeType: NodeType.Client);

        var nonExistentChunkId = $"c-{Guid.NewGuid():N}";
        var validToken = SignDataToken(_fixture.TokenKey, "admin", nonExistentChunkId, "r");

        var readReq = new ReadChunk(Guid.NewGuid().ToString("D"), nonExistentChunkId, validToken);
        await rawDs.SendMessageAsync(MessageType.ReadChunk, readReq);

        var errFrame = await rawDs.ReceiveRawFrameAsync();
        Assert.NotNull(errFrame);
        Assert.Equal(MessageType.Error, errFrame.Type);
        var err = FrameSerialization.DeserializeJson<ErrorResponse>(errFrame.Payload);
        Assert.Equal(ErrorCode.NotFound, err.Code);
    }

    #endregion
}
