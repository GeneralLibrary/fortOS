using GORT.Core;
using GORT.Security.Services;

namespace GORT.Tests.Integration.Security;

public class IdentityServiceTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task LocalUser_CreateAuthenticateWrongPasswordAndLockout()
    {
        using var fixture = new SecurityFixture();
        var manager = fixture.CreateTokenManager();
        var identity = new IdentityService(fixture.Database, manager);
        var ct = CancellationToken.None;

        var created = await identity.CreateLocalUserAsync("alice", "Password1", "Alice", "alice@example.test", ct);
        Assert.True(created.Success, created.ErrorMessage);

        var success = await identity.AuthenticateLocalAsync("alice", "Password1", ct);
        Assert.True(success.Success, success.ErrorMessage);
        Assert.NotNull(success.NasToken);

        var wrong = await identity.AuthenticateLocalAsync("alice", "bad", ct);
        Assert.False(wrong.Success);

        for (var i = 0; i < 4; i++)
        {
            await identity.AuthenticateLocalAsync("alice", "bad", ct);
        }

        var locked = await identity.AuthenticateLocalAsync("alice", "Password1", ct);
        Assert.False(locked.Success);
        Assert.Contains("locked", locked.ErrorMessage);
    }
}
