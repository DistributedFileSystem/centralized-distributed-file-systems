using Dfs.Shared.Messages;
using Dfs.Shared.Metadata;
using CreateUser = Dfs.Shared.Commands.CreateUser;

namespace Dfs.MetadataServer.Security;

/// <summary>Creates user credentials through the metadata command boundary and verifies stored passwords.</summary>
public sealed class UserCredentialService(IMetadataStore store)
{
    /// <summary>Hashes the password before submitting the deterministic CreateUser command.</summary>
    public Task<CommandResult> CreateUserAsync(string userId, string username, string password, DateTime at,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (passwordHash, salt) = PasswordHasher.HashPassword(password);
        return store.SubmitAsync(new CreateUser(userId, username, passwordHash, salt, at), cancellationToken);
    }

    /// <summary>Checks a password against the stored credentials; disabled or unknown users fail.</summary>
    public async Task<bool> VerifyPasswordAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var user = await store.FindUserByNameAsync(username, cancellationToken);
        return user is { Disabled: false } && PasswordHasher.VerifyPassword(password, user.PasswordHash, user.Salt);
    }
}
