using GORT.Agent.Broker;
using GORT.Core;
using GORT.Security.KeyStore;
using GORT.Security.Services;

namespace GORT.Tests.Integration.Agent;

public class TokenBrokerTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task OwnerCanIssueNarrowedStorageCapability()
    {
        using var root = new AgentTestDataRoot(nameof(OwnerCanIssueNarrowedStorageCapability));
        var database = new DatabaseProvider(root.Root);
        var manager = new NasTokenManager(new NasKeyStore(), database);
        var logs = new CapturingLogPipeline();
        var broker = new TokenBroker(manager, logs);
        var owner = await manager.IssueTokenAsync("user:alice", TokenType.Session, ["storage:**"], 5, TimeSpan.FromHours(1), ["user:alice"], null, CancellationToken.None);
        var config = new AgentConfig
        {
            AgentId = "indexer",
            DisplayName = "Indexer",
            ImageName = "indexer:latest",
            Capabilities = ["storage:share:media:read"],
        };

        var result = await broker.IssueAgentTokenAsync(config, owner, CancellationToken.None);
        var validation = await manager.ValidateTokenAsync(result.Token, CancellationToken.None);

        Assert.True(validation.IsValid);
        Assert.Equal(TokenType.Agent, validation.TokenType);
        Assert.Equal("agent:indexer", validation.Subject);
        Assert.Equal(["storage:share:media:read"], result.Capabilities);
        Assert.Contains(logs.Entries, e => e.Audit?.Action == "agent.token.issue" && e.Audit.Granted);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task OwnerWithoutAdminCannotIssueAdminCapability()
    {
        using var root = new AgentTestDataRoot(nameof(OwnerWithoutAdminCannotIssueAdminCapability));
        var database = new DatabaseProvider(root.Root);
        var manager = new NasTokenManager(new NasKeyStore(), database);
        var logs = new CapturingLogPipeline();
        var broker = new TokenBroker(manager, logs);
        var owner = await manager.IssueTokenAsync("user:alice", TokenType.Session, ["storage:**"], 5, TimeSpan.FromHours(1), ["user:alice"], null, CancellationToken.None);
        var config = new AgentConfig
        {
            AgentId = "admin-agent",
            DisplayName = "Admin Agent",
            ImageName = "admin:latest",
            Capabilities = ["admin:**"],
        };

        await Assert.ThrowsAsync<PermissionDeniedException>(() => broker.IssueAgentTokenAsync(config, owner, CancellationToken.None));
        Assert.Contains(logs.Entries, e => e.Audit?.Action == "agent.token.issue" && !e.Audit.Granted);
    }
}
