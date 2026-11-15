using Dfs.MetadataServer.Security;
using Dfs.MetadataServer.Store;
using Dfs.Shared.Commands;
using Dfs.Shared.Messages;
using Dfs.Shared.Security;
using SetUserDisabled = Dfs.Shared.Commands.SetUserDisabled;

namespace Dfs.Tests.Security;

public sealed class SessionServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"dfs-session-{Guid.NewGuid():N}");
    private readonly TestTimeProvider _time = new(new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero));

    private MetadataStore Open() => new(Path.Combine(_directory, "oplog.jsonl"), Path.Combine(_directory, "metadata.db"));

    [Fact]
    public async Task First_admin_is_created_once_with_a_grant_and_an_eight_hour_session()
    {
        using var store = Open();
        var service = new SessionService(store, _time);
        await service.BootstrapAdminAsync("admin", "initial-secret");

        var saved = await store.FindUserByNameAsync("admin");
        Assert.NotNull(saved);
        Assert.True(PasswordHasher.VerifyPassword("initial-secret", saved.PasswordHash, saved.Salt));
        Assert.DoesNotContain("initial-secret", File.ReadAllText(Path.Combine(_directory, "oplog.jsonl")));
        Assert.Single(await store.ListUsersAsync());
        var grant = Assert.Single(await store.ListGrantsAsync(null, saved.Id, true));
        Assert.Equal(BuiltInRoles.AdminId, grant.RoleId);

        var login = await service.LoginAsync(new Login("login-1", "admin", "initial-secret"));
        Assert.Equal("login-1", login.RequestId);
        Assert.StartsWith("s-", login.Session);
        Assert.Matches("^s-[A-Za-z0-9_-]{43}$", login.Session);
        Assert.Equal(_time.GetUtcNow().UtcDateTime.AddHours(8), login.ExpiresAt);
        Assert.Equal(saved.Id, login.User.Id);
        Assert.Contains(BuiltInRoles.Admin, login.SystemRoles);
        Assert.Contains(Permission.UserManage, login.Permissions);
        Assert.Equal(saved.Id, (await store.FindSessionAsync(login.Session))?.UserId);

        await service.BootstrapAdminAsync("changed-admin", "changed-password");
        Assert.Single(await store.ListUsersAsync());
        Assert.Null(await store.FindUserByNameAsync("changed-admin"));
        Assert.Equal(saved, await store.FindUserByNameAsync("admin"));
    }

    [Fact]
    public async Task Wrong_credentials_disabled_user_and_existing_session_are_rejected()
    {
        using var store = Open();
        var service = new SessionService(store, _time);
        await service.BootstrapAdminAsync("admin", "secret");
        var user = (await store.FindUserByNameAsync("admin"))!;
        var session = await service.LoginAsync(new Login("ok", "admin", "secret"));

        foreach (var request in new[] { new Login("a", "missing", "secret"), new Login("b", "admin", "wrong") })
        {
            var error = await Assert.ThrowsAsync<DfsException>(() => service.LoginAsync(request));
            Assert.Equal(ErrorCode.Unauthorized, error.Code);
        }

        Assert.True((await store.SubmitAsync(new SetUserDisabled(user.Id, true, _time.GetUtcNow().UtcDateTime))).Success);
        var disabled = await Assert.ThrowsAsync<DfsException>(() => service.LoginAsync(new Login("c", "admin", "secret")));
        Assert.Equal(ErrorCode.Unauthorized, disabled.Code);
        var revoked = await Assert.ThrowsAsync<DfsException>(() => service.RequireActiveUserAsync(session.Session));
        Assert.Equal(ErrorCode.Unauthorized, revoked.Code);
    }

    [Fact]
    public async Task Session_survives_restart_expires_at_boundary_and_logout_is_persistent()
    {
        string token;
        using (var store = Open())
        {
            var service = new SessionService(store, _time);
            await service.BootstrapAdminAsync("admin", "secret");
            token = (await service.LoginAsync(new Login("one", "admin", "secret"))).Session;
        }

        using (var store = Open())
        {
            var service = new SessionService(store, _time);
            Assert.Equal("admin", (await service.RequireActiveUserAsync(token)).Username);
            _time.Advance(TimeSpan.FromHours(8));
            var expired = await Assert.ThrowsAsync<DfsException>(() => service.RequireActiveUserAsync(token));
            Assert.Equal(ErrorCode.Unauthorized, expired.Code);

            _time.Advance(TimeSpan.FromHours(-8));
            var logout = await service.LogoutAsync(new Logout("logout-1", token));
            Assert.Equal("logout-1", logout.RequestId);
            Assert.Null(await store.FindSessionAsync(token));
        }

        using var reopened = Open();
        Assert.Null(await reopened.FindSessionAsync(token));
    }

    [Fact]
    public async Task Empty_bootstrap_password_fails_on_fresh_store()
    {
        using var store = Open();
        var service = new SessionService(store, _time);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.BootstrapAdminAsync("admin", ""));
        Assert.Empty(await store.ListUsersAsync());
    }

    [Fact]
    public async Task Interrupted_bootstrap_completes_admin_grant_without_resetting_password()
    {
        using var store = Open();
        var credentials = new UserCredentialService(store);
        Assert.True((await credentials.CreateUserAsync("u-bootstrap-admin", "admin", "original",
            _time.GetUtcNow().UtcDateTime)).Success);

        var service = new SessionService(store, _time);
        await service.BootstrapAdminAsync("different-name", "different-password");

        Assert.Single(await store.ListUsersAsync());
        Assert.True(await credentials.VerifyPasswordAsync("admin", "original"));
        Assert.False(await credentials.VerifyPasswordAsync("admin", "different-password"));
        Assert.Equal(BuiltInRoles.AdminId,
            Assert.Single(await store.ListGrantsAsync(null, "u-bootstrap-admin", true)).RoleId);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
