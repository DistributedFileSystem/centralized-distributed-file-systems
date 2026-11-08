using Dfs.MetadataServer.Security;
using Dfs.MetadataServer.Store;

namespace Dfs.Tests.Security;

public class PasswordHasherTests
{
    [Fact]
    public void Same_password_uses_distinct_salts_and_verifies_only_the_correct_password()
    {
        var first = PasswordHasher.HashPassword("correct horse battery staple");
        var second = PasswordHasher.HashPassword("correct horse battery staple");

        Assert.NotEqual(first.Salt, second.Salt);
        Assert.NotEqual(first.PasswordHash, second.PasswordHash);
        Assert.StartsWith("pbkdf2-sha256$600000$", first.PasswordHash);
        Assert.True(PasswordHasher.VerifyPassword("correct horse battery staple", first.PasswordHash, first.Salt));
        Assert.True(PasswordHasher.VerifyPassword("correct horse battery staple", second.PasswordHash, second.Salt));
        Assert.False(PasswordHasher.VerifyPassword("wrong password", first.PasswordHash, first.Salt));
        Assert.False(PasswordHasher.VerifyPassword("correct horse battery staple", first.PasswordHash, second.Salt));
        Assert.False(PasswordHasher.VerifyPassword("correct horse battery staple", "malformed", first.Salt));
    }

    [Fact]
    public async Task User_credentials_survive_store_restart_and_verify_against_saved_hash()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dfs-password-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var logPath = Path.Combine(directory, "oplog.jsonl");
        var dbPath = Path.Combine(directory, "metadata.db");

        try
        {
            using (var store = new MetadataStore(logPath, dbPath))
            {
                var users = new UserCredentialService(store);
                var result = await users.CreateUserAsync("u-alice", "alice", "my secret password", DateTime.UtcNow);
                Assert.True(result.Success);

                var saved = await store.FindUserByNameAsync("alice");
                Assert.NotNull(saved);
                Assert.NotEqual("my secret password", saved.PasswordHash);
                Assert.NotEqual("my secret password", saved.Salt);
                Assert.DoesNotContain("my secret password", File.ReadAllText(logPath), StringComparison.Ordinal);
                Assert.True(await users.VerifyPasswordAsync("alice", "my secret password"));
                Assert.False(await users.VerifyPasswordAsync("alice", "wrong"));
                Assert.False(await users.VerifyPasswordAsync("unknown", "my secret password"));
            }

            using (var reopened = new MetadataStore(logPath, dbPath))
            {
                var users = new UserCredentialService(reopened);
                Assert.True(await users.VerifyPasswordAsync("alice", "my secret password"));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
