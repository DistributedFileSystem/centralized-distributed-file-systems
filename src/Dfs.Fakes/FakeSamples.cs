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
    internal static readonly VersionInfo[] Versions =
    [
        new(3, 14_889_779, "an.nguyen", new DateTime(2026, 11, 4, 16, 5, 0, DateTimeKind.Utc), 4),
        new(2, 12_582_912, "tan.phan", new DateTime(2026, 11, 4, 14, 15, 0, DateTimeKind.Utc), 3),
        new(1, 10_276_044, "an.nguyen", new DateTime(2026, 11, 3, 20, 30, 0, DateTimeKind.Utc), 3),
    ];
}
