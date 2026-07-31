using FortOS.Core;
using FortOS.Security.Models;
using Microsoft.Extensions.Logging;

namespace FortOS.Agent.Broker;

/// <summary>
/// Performs capability narrowing and auditing for Agent token issuance,
/// renewal, and revocation.
/// </summary>
public sealed class TokenBroker : ITokenBroker
{
    private static readonly TimeSpan AgentLifetime = TimeSpan.FromHours(24);
    private readonly ITokenManager _tokenManager;
    private readonly ILogPipeline _logPipeline;
    private readonly AgentTokenRegistry _registry;

    /// <summary>
    /// Initialize the Agent token broker.
    /// </summary>
    public TokenBroker(ITokenManager tokenManager, ILogPipeline logPipeline, AgentTokenRegistry? registry = null)
    {
        _tokenManager = tokenManager;
        _logPipeline = logPipeline;
        _registry = registry ?? new AgentTokenRegistry();
    }

    /// <inheritdoc />
    public async Task<AgentTokenResult> IssueAgentTokenAsync(AgentConfig config, string ownerToken, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(config);
        var granted = false;
        try
        {
            var owner = await _tokenManager.ValidateTokenAsync(ownerToken, ct).ConfigureAwait(false);
            if (!owner.IsValid)
            {
                throw new TokenValidationException(owner.ErrorMessage ?? "Owner token is invalid.");
            }

            var ownerSet = BuildAbilitySet(owner.Capabilities);
            EnsureCapabilitiesAllowed(config.Capabilities, ownerSet);
            var trustLevel = Math.Min(GetTrustLevel(owner), 2);
            var token = await _tokenManager.IssueTokenAsync($"agent:{config.AgentId}", TokenType.Agent, config.Capabilities, trustLevel, AgentLifetime, [owner.Subject ?? "owner", $"agent:{config.AgentId}"], null, ct).ConfigureAwait(false);
            var validation = await _tokenManager.ValidateTokenAsync(token, ct).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            var result = new AgentTokenResult
            {
                Token = token,
                AgentId = config.AgentId,
                Capabilities = [.. config.Capabilities],
                IssuedAt = now,
                ExpiresAt = validation.ExpiresAt ?? now.Add(AgentLifetime),
            };
            _registry.Upsert(new AgentTokenState(config.AgentId, token, validation.Jti, result.ExpiresAt, result.Capabilities));
            granted = true;
            return result;
        }
        finally
        {
            await WriteAuditAsync("agent.token.issue", config.AgentId, granted, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<AgentTokenResult> RenewAgentTokenAsync(string agentId, string token, CancellationToken ct)
    {
        var granted = false;
        try
        {
            var renewed = await _tokenManager.RenewTokenAsync(token, ct).ConfigureAwait(false);
            var validation = await _tokenManager.ValidateTokenAsync(renewed, ct).ConfigureAwait(false);
            if (!validation.IsValid)
            {
                throw new TokenValidationException(validation.ErrorMessage ?? "Renewed Agent token is invalid.");
            }

            var now = DateTimeOffset.UtcNow;
            var result = new AgentTokenResult
            {
                Token = renewed,
                AgentId = agentId,
                Capabilities = [.. validation.Capabilities],
                IssuedAt = now,
                ExpiresAt = validation.ExpiresAt ?? now.Add(AgentLifetime),
            };
            _registry.Upsert(new AgentTokenState(agentId, renewed, validation.Jti, result.ExpiresAt, result.Capabilities));
            granted = true;
            return result;
        }
        finally
        {
            await WriteAuditAsync("agent.token.renew", agentId, granted, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task RevokeAgentTokenAsync(string agentId, string reason, CancellationToken ct)
    {
        var granted = false;
        try
        {
            if (!_registry.Remove(agentId, out var state) || string.IsNullOrWhiteSpace(state?.Jti))
            {
                throw new TokenValidationException($"Agent {agentId} has no known revocable token.");
            }

            await _tokenManager.RevokeTokenAsync(state.Jti, reason, ct).ConfigureAwait(false);
            granted = true;
        }
        finally
        {
            await WriteAuditAsync("agent.token.revoke", agentId, granted, ct).ConfigureAwait(false);
        }
    }

    private static NAbilitySet BuildAbilitySet(IEnumerable<string> capabilities)
    {
        var set = new NAbilitySet();
        foreach (var capability in capabilities)
        {
            set.Add(capability);
        }

        return set;
    }

    private static void EnsureCapabilitiesAllowed(IEnumerable<string> requested, NAbilitySet ownerSet)
    {
        foreach (var capability in requested)
        {
            var required = NAbility.Parse(capability);
            var isAdmin = string.Equals(required.Domain, "admin", StringComparison.OrdinalIgnoreCase);
            if (isAdmin && !ownerSet.Satisfies("admin:**"))
            {
                throw new PermissionDeniedException($"Owner token is not allowed to delegate administrative capability to Agent: {capability}.");
            }

            if (!ownerSet.Satisfies(required))
            {
                throw new PermissionDeniedException($"Owner token does not satisfy the Agent requested capability: {capability}.");
            }
        }
    }

    private static int GetTrustLevel(TokenValidationResult validation)
        => validation.Payload is NasTokenPayload payload ? payload.TrustLevel : 0;

    private Task WriteAuditAsync(string action, string agentId, bool granted, CancellationToken ct)
        => _logPipeline.ProcessAsync(new LogEntry
        {
            Category = LogCategory.Audit,
            Level = granted ? LogLevel.Information : LogLevel.Warning,
            SourceComponent = "FortOS.Agent.TokenBroker",
            AgentId = agentId,
            Message = granted ? $"Agent token operation completed: {action} for {agentId}." : $"Agent token operation denied: {action} for {agentId}.",
            Audit = new AuditDetail
            {
                Action = action,
                Resource = agentId,
                ResourceType = "agent-token",
                Granted = granted,
                CurrentHash = string.Empty,
                ChainSignature = string.Empty,
            },
        }, ct);
}
