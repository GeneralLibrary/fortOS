using GNAS.Core;

namespace GNAS.Tests.Integration.Agent;

internal sealed class CapturingLogPipeline : ILogPipeline
{
    public List<LogEntry> Entries { get; } = [];
    public List<string> Raw { get; } = [];
    public Task ProcessAsync(LogEntry entry, CancellationToken ct)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task ProcessRawAsync(string rawText, LogCategory category, string sourceComponent, CancellationToken ct)
    {
        Raw.Add(rawText);
        return Task.CompletedTask;
    }
}

internal sealed class FixedTokenBroker : ITokenBroker
{
    private readonly string _token;

    public FixedTokenBroker(string token) => _token = token;

    public Task<AgentTokenResult> IssueAgentTokenAsync(AgentConfig config, string ownerToken, CancellationToken ct) => Task.FromResult(new AgentTokenResult
    {
        AgentId = config.AgentId,
        Token = _token,
        Capabilities = [.. config.Capabilities],
        IssuedAt = DateTimeOffset.UtcNow,
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(24),
    });

    public Task<AgentTokenResult> RenewAgentTokenAsync(string agentId, string token, CancellationToken ct) => Task.FromResult(new AgentTokenResult
    {
        AgentId = agentId,
        Token = _token,
        Capabilities = [],
        IssuedAt = DateTimeOffset.UtcNow,
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(24),
    });

    public Task RevokeAgentTokenAsync(string agentId, string reason, CancellationToken ct) => Task.CompletedTask;
}

internal sealed class AgentTestDataRoot : IDisposable
{
    private readonly string? _previous;

    public AgentTestDataRoot(string name)
    {
        _previous = Environment.GetEnvironmentVariable("GNAS_DATA_ROOT");
        Root = Path.GetFullPath(Path.Combine("TestArtifacts", "Agent", name, Guid.CreateVersion7().ToString()));
        Directory.CreateDirectory(Root);
        Environment.SetEnvironmentVariable("GNAS_DATA_ROOT", Root);
    }

    public string Root { get; }

    public void Dispose() => Environment.SetEnvironmentVariable("GNAS_DATA_ROOT", _previous);
}
