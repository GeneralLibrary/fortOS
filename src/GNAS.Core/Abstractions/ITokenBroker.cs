namespace GNAS.Core;

/// <summary>Agent token broker interface.</summary>
public interface ITokenBroker
{
    /// <summary>Issue an agent token.</summary>
    Task<AgentTokenResult> IssueAgentTokenAsync(AgentConfig config, string ownerToken, CancellationToken ct);
    /// <summary>Renew an agent token.</summary>
    Task<AgentTokenResult> RenewAgentTokenAsync(string agentId, string token, CancellationToken ct);
    /// <summary>Revoke an agent token.</summary>
    Task RevokeAgentTokenAsync(string agentId, string reason, CancellationToken ct);
}
