namespace Dfs.Shared.Security;

/// <summary>Decision of <see cref="IAuthorizer"/>.</summary>
/// <param name="Allowed">True when the permission is granted.</param>
/// <param name="Reason">Short explanation, for the audit log.</param>
public sealed record AuthorizationResult(bool Allowed, string Reason)
{
    /// <summary>Creates an allow decision.</summary>
    /// <param name="reason">Short explanation.</param>
    /// <returns>An allowed result.</returns>
    public static AuthorizationResult Allow(string reason) => new(true, reason);

    /// <summary>Creates a deny decision.</summary>
    /// <param name="reason">Short explanation.</param>
    /// <returns>A denied result.</returns>
    public static AuthorizationResult Deny(string reason) => new(false, reason);
}
