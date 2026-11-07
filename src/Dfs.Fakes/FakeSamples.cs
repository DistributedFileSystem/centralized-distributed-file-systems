using System.Security.Cryptography;
using System.Text;
using Dfs.Shared.Messages;

namespace Dfs.Fakes;

internal static class FakeSamples
{
    internal const string UserId = "u-demo";
    internal const string ChunkId = "c-demo-readme";
    internal const string FilePath = "/projects/dfs/readme.txt";
    internal static readonly byte[] Content = Encoding.UTF8.GetBytes("Distributed file system demo\n");
    internal static readonly string Sha256 = Convert.ToHexStringLower(SHA256.HashData(Content));
    private static readonly DateTime ModifiedAt = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    internal static readonly ItemInfo[] Items =
    [
        new("i-root", "/", "/", ItemKind.Folder, 0, UserId, 0, ModifiedAt),
        new("i-projects", "/projects", "projects", ItemKind.Folder, 0, UserId, 0, ModifiedAt),
        new("i-dfs", "/projects/dfs", "dfs", ItemKind.Folder, 0, UserId, 0, ModifiedAt),
        new("i-readme", FilePath, "readme.txt", ItemKind.File, Content.Length, UserId, 1, ModifiedAt),
    ];

    internal static readonly VersionInfo Version = new(1, Content.Length, UserId, ModifiedAt, 1);
}
