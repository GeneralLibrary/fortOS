using GNAS.Core;
using GNAS.Security.Services;

namespace GNAS.Tests.Integration.Security;

public class PermissionEngineTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task PermissionEngine_GrantsAndDeniesByCapabilityAndTrustLevel()
    {
        using var fixture = new SecurityFixture();
        var manager = fixture.CreateTokenManager();
        var engine = new PermissionEngine(manager, fixture.Database);
        var ct = CancellationToken.None;
        var token = await manager.IssueTokenAsync("user:alice", TokenType.Session, ["storage:share:media:read"], 2, TimeSpan.FromHours(1), ["user:alice"], null, ct);

        var granted = await engine.CheckPermissionAsync(token, "storage:share:media:read", "/data/media", NasDataLevel.Personal, ct);
        Assert.True(granted.Granted, granted.DenyReason);
        Assert.Equal("storage:share:media:read", granted.MatchedCapability);

        var deniedByCapability = await engine.CheckPermissionAsync(token, "storage:share:media:write", "/data/media", NasDataLevel.Personal, ct);
        Assert.False(deniedByCapability.Granted);
        Assert.Contains("能力", deniedByCapability.DenyReason);

        var deniedByTrust = await engine.CheckPermissionAsync(token, "storage:share:media:read", "/data/media", NasDataLevel.Sensitive, ct);
        Assert.False(deniedByTrust.Granted);
        Assert.Contains("信任", deniedByTrust.DenyReason);
    }
}
