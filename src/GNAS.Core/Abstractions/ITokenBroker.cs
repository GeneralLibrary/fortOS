namespace GNAS.Core;

/// <summary>Agent 令牌代理接口。</summary>
public interface ITokenBroker
{
    /// <summary>签发 Agent 令牌。</summary>
    Task<AgentTokenResult> IssueAgentTokenAsync(AgentConfig config, string ownerToken, CancellationToken ct);
    /// <summary>续期 Agent 令牌。</summary>
    Task<AgentTokenResult> RenewAgentTokenAsync(string agentId, string token, CancellationToken ct);
    /// <summary>吊销 Agent 令牌。</summary>
    Task RevokeAgentTokenAsync(string agentId, string reason, CancellationToken ct);
}
