namespace Dfs.Shared.Security;

/// <summary>Pure allow/deny decision for one permission; it writes nothing and does not depend on the store (the caller records audit).</summary>
public interface IAuthorizer
{
    /// <summary>Checks whether a user has a permission, system wide or on an item (grants inherit down the tree).</summary>
    /// <param name="userId">Id of the user (from the session).</param>
    /// <param name="permission">Permission name from <see cref="Permission"/>.</param>
    /// <param name="itemId">Id of the item to check; null for a system permission.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The decision with a reason.</returns>
    Task<AuthorizationResult> AuthorizeAsync(string userId, string permission, string? itemId, CancellationToken cancellationToken = default);
}
