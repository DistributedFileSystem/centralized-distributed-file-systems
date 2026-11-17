using System.Security.Cryptography;
using Dfs.DataServer.Storage;
using Dfs.Shared.Messages;

namespace Dfs.Tests.DataServer;

public sealed class ChunkStorageTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ChunkStorage _storage;

    public ChunkStorageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dfs-chunk-storage-tests-" + Guid.NewGuid().ToString("N"));
        _storage = new ChunkStorage(_tempDir, configuredCapacity: 50L * 1024 * 1024 * 1024);
    }

    public void Dispose()
    {
        _storage.Dispose();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public async Task WriteChunk_AtomicallyWrites_AndCalculatesCorrectSha256()
    {
        var chunkId = "c-01HABCDEF123456789";
        var data = RandomNumberGenerator.GetBytes(64 * 1024); // 64 KB
        var expectedSha256 = Convert.ToHexStringLower(SHA256.HashData(data));

        var result = await _storage.WriteChunkAsync(chunkId, data);

        Assert.Equal(data.Length, result.Size);
        Assert.Equal(expectedSha256, result.Sha256);

        var finalPath = _storage.GetChunkFilePath(chunkId);
        Assert.True(File.Exists(finalPath));
        Assert.Equal(data, await File.ReadAllBytesAsync(finalPath));

        var count = await _storage.GetChunkCountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task WriteChunk_Idempotent_WhenRewrittenWithSameOrNewContent()
    {
        var chunkId = "c-01HREWRITE12345678";
        var data1 = new byte[] { 1, 2, 3, 4, 5 };
        var data2 = new byte[] { 1, 2, 3, 4, 5, 6, 7 };

        var result1 = await _storage.WriteChunkAsync(chunkId, data1);
        Assert.Equal(5, result1.Size);

        var result2 = await _storage.WriteChunkAsync(chunkId, data2);
        Assert.Equal(7, result2.Size);

        var read = await _storage.ReadChunkAsync(chunkId);
        Assert.Equal(data2, read.Data);
        Assert.Equal(7, read.Size);

        var count = await _storage.GetChunkCountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task ReadChunk_ReturnsExactBytesAndSha256()
    {
        var chunkId = "c-01HREADTEST1234567";
        var data = "Hello, Distributed File System!"u8.ToArray();
        var expectedSha256 = Convert.ToHexStringLower(SHA256.HashData(data));

        await _storage.WriteChunkAsync(chunkId, data);
        var read = await _storage.ReadChunkAsync(chunkId);

        Assert.Equal(data, read.Data);
        Assert.Equal(data.Length, read.Size);
        Assert.Equal(expectedSha256, read.Sha256);
    }

    [Fact]
    public async Task ReadChunk_NotFound_ThrowsDfsExceptionNotFound()
    {
        var ex = await Assert.ThrowsAsync<DfsException>(() => _storage.ReadChunkAsync("c-non-existent-chunk"));
        Assert.Equal(ErrorCode.NotFound, ex.Code);
    }

    [Fact]
    public async Task DeleteChunks_DeletesFilesAndRemovesFromIndex_Idempotent()
    {
        var chunk1 = "c-01HDELETE111111111";
        var chunk2 = "c-01HDELETE222222222";
        await _storage.WriteChunkAsync(chunk1, new byte[] { 1, 2, 3 });
        await _storage.WriteChunkAsync(chunk2, new byte[] { 4, 5, 6 });

        Assert.Equal(2, await _storage.GetChunkCountAsync());

        var deleted = await _storage.DeleteChunksAsync([chunk1, chunk2, "c-already-gone"]);
        Assert.Equal(2, deleted);
        Assert.Equal(0, await _storage.GetChunkCountAsync());

        Assert.False(File.Exists(_storage.GetChunkFilePath(chunk1)));
        Assert.False(File.Exists(_storage.GetChunkFilePath(chunk2)));

        // Idempotent delete on already deleted chunks
        var deletedAgain = await _storage.DeleteChunksAsync([chunk1]);
        Assert.Equal(0, deletedAgain);
    }

    [Fact]
    public async Task ListChunks_ReturnsAllStoredChunks()
    {
        var chunk1 = "c-01HLIST11111111111";
        var chunk2 = "c-01HLIST22222222222";
        await _storage.WriteChunkAsync(chunk1, new byte[] { 10, 20 });
        await _storage.WriteChunkAsync(chunk2, new byte[] { 30, 40, 50 });

        var list = await _storage.ListChunksAsync();
        Assert.Equal(2, list.Count);
        Assert.Contains(list, c => c.ChunkId == chunk1 && c.Size == 2);
        Assert.Contains(list, c => c.ChunkId == chunk2 && c.Size == 3);
    }

    [Fact]
    public async Task CapacityAndFreeBytes_ReturnNonZeroValues()
    {
        var capacity = await _storage.GetCapacityBytesAsync();
        var free = await _storage.GetFreeBytesAsync();

        Assert.True(capacity > 0);
        Assert.True(free > 0);
    }
}
