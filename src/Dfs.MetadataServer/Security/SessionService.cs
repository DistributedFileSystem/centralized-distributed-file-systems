using System.Security.Cryptography;
using Dfs.Shared.Commands;
using Dfs.Shared.Messages;
using Dfs.Shared.Metadata;
using Dfs.Shared.Security;
using CreateRole = Dfs.Shared.Commands.CreateRole;

namespace Dfs.MetadataServer.Security;

/// <summary>Bootstraps the first admin and handles login, session validation, and logout.</summary>
public sealed class SessionService(IMetadataStore store, TimeProvider? timeProvider = null)
{
    private const string BootstrapUserId = "u-bootstrap-admin";
    private const string BootstrapGrantId = "g-bootstrap-admin";
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <summary>Creates the first admin once, and completes its grant after an interrupted startup.</summary>
    public async Task BootstrapAdminAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var bootstrapUser = await store.FindUserByIdAsync(BootstrapUserId, cancellationToken);
        if (bootstrapUser is null)
        {
            if ((await store.ListUsersAsync(cancellationToken)).Count != 0) return;
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
                throw new InvalidOperationException("ADMIN_USER and ADMIN_PASSWORD are required for the first admin.");
        }

        var roles = await store.ListRolesAsync(cancellationToken);
        if (roles.All(r => r.Id != BuiltInRoles.AdminId))
        {
            var permissions = new ValueList<string>([
                Permission.UserManage, Permission.GroupManage, Permission.RoleManage,
                Permission.ClusterView, Permission.ClusterManage, Permission.AuditView,
            ]);
            await RequireSuccessAsync(new CreateRole(BuiltInRoles.AdminId, BuiltInRoles.Admin,
                RoleScope.System, permissions, Now()), cancellationToken);
        }

        if (bootstrapUser is null)
        {
            var result = await new UserCredentialService(store).CreateUserAsync(
                BootstrapUserId, username, password, Now(), cancellationToken);
            if (!result.Success)
                throw new InvalidOperationException($"Admin bootstrap failed: {result.Message}");
        }

        var grants = await store.ListGrantsAsync(null, BootstrapUserId, true, cancellationToken);
        if (grants.All(g => g.RoleId != BuiltInRoles.AdminId))
            await RequireSuccessAsync(new SetGrant(BootstrapGrantId, BuiltInRoles.AdminId,
                SubjectType.User, BootstrapUserId, null, Now()), cancellationToken);
    }

    /// <summary>Verifies credentials and returns a server-backed session valid for eight hours.</summary>
    public async Task<LoginResponse> LoginAsync(Login request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RequestId))
            throw new DfsException(ErrorCode.BadRequest, "RequestId is required.");

        var user = string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password)
            ? null : await store.FindUserByNameAsync(request.Username, cancellationToken);
        if (user is null || user.Disabled || !PasswordHasher.VerifyPassword(request.Password!, user.PasswordHash, user.Salt))
            throw new DfsException(ErrorCode.Unauthorized, "Invalid username or password.");

        var now = Now();
        var expiry = now.Add(SessionLifetime);
        var token = "s-" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var result = await store.SubmitAsync(new CreateSession(token, user.Id, expiry, now), cancellationToken);
        if (!result.Success)
            throw new DfsException(result.Error ?? ErrorCode.BadRequest, result.Message ?? "Session could not be created.");

        var grants = await store.ListGrantsAsync(null, user.Id, true, cancellationToken);
        var roles = await store.ListRolesAsync(cancellationToken);
        var grantedRoles = roles.Where(r => grants.Any(g => g.RoleId == r.Id)).ToArray();
        return new LoginResponse(request.RequestId, token, expiry, new UserRef(user.Id, user.Username),
            new ValueList<string>(grantedRoles.Select(r => r.Name)),
            new ValueList<string>(grantedRoles.SelectMany(r => r.Permissions).Distinct(StringComparer.Ordinal)));
    }

    /// <summary>Finds the active user; expired sessions and disabled users are unauthorized.</summary>
    public async Task<UserCredentials> RequireActiveUserAsync(string token, CancellationToken cancellationToken = default)
    {
        var session = string.IsNullOrWhiteSpace(token) ? null : await store.FindSessionAsync(token, cancellationToken);
        var user = session is null ? null : await store.FindUserByIdAsync(session.UserId, cancellationToken);
        if (session is null || session.ExpiresAt <= Now() || user is null || user.Disabled)
            throw new DfsException(ErrorCode.Unauthorized, "Session is missing or expired.");
        return user;
    }

    /// <summary>Invalidates an active session.</summary>
    public async Task<LogoutResponse> LogoutAsync(Logout request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RequestId))
            throw new DfsException(ErrorCode.BadRequest, "RequestId is required.");
        await RequireActiveUserAsync(request.Session, cancellationToken);
        var result = await store.SubmitAsync(new DeleteSession(request.Session, Now()), cancellationToken);
        if (!result.Success)
            throw new DfsException(result.Error ?? ErrorCode.BadRequest, result.Message ?? "Session could not be deleted.");
        return new LogoutResponse(request.RequestId);
    }

    private DateTime Now() => _time.GetUtcNow().UtcDateTime;

    private async Task RequireSuccessAsync(IMetadataCommand command, CancellationToken cancellationToken)
    {
        var result = await store.SubmitAsync(command, cancellationToken);
        if (!result.Success)
            throw new InvalidOperationException($"Admin bootstrap failed: {result.Message}");
    }
}
