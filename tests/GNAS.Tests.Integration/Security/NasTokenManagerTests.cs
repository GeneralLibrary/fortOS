using GNAS.Core;
using GNAS.Security.KeyStore;
using GNAS.Security.Models;
using GNAS.Security.Services;

namespace GNAS.Tests.Integration.Security;

public class NasTokenManagerTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task IssueValidateRevokeRenewAndRejectInvalidTokens()
    {
        using var fixture = new SecurityFixture();
        var manager = fixture.CreateTokenManager();
        var ct = CancellationToken.None;

        var token = await manager.IssueTokenAsync("user:alice", TokenType.Session, ["storage:share:media:read"], 3, TimeSpan.FromHours(1), ["user:alice"], null, ct);
        var validation = await manager.ValidateTokenAsync(token, ct);
        Assert.True(validation.IsValid);
        Assert.Equal("user:alice", validation.Subject);
        Assert.Contains("storage:share:media:read", validation.Capabilities);

        var renewed = await manager.RenewTokenAsync(token, ct);
        var oldValidation = await manager.ValidateTokenAsync(token, ct);
        var renewedValidation = await manager.ValidateTokenAsync(renewed, ct);
        Assert.False(oldValidation.IsValid);
        Assert.True(renewedValidation.IsValid);

        await manager.RevokeTokenAsync(renewedValidation.Jti!, "test", ct);
        Assert.True(await manager.IsTokenRevokedAsync(renewedValidation.Jti!, ct));
        Assert.False((await manager.ValidateTokenAsync(renewed, ct)).IsValid);

        var expired = await manager.IssueTokenAsync("user:alice", TokenType.Session, [NAbilityConstants.DataInternal], 1, TimeSpan.FromSeconds(-1), null, null, ct);
        Assert.False((await manager.ValidateTokenAsync(expired, ct)).IsValid);

        var tampered = renewed[..^1] + (renewed[^1] == 'a' ? 'b' : 'a');
        Assert.False((await manager.ValidateTokenAsync(tampered, ct)).IsValid);
    }
}
