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
    private readonly ITokenManager _tokenManager;
    private readonly ILogPipeline _logPipeline;
    private readonly AgentTokenRegistry _registry;
    private readonly ILogger<TokenBroker>? _logger;

    /// <summary>
    /// Initialize the Agent token broker.
    /// </summary>
    public TokenBroker(ITokenManager tokenManager, ILogPipeline logPipeline, AgentTokenRegistry? registry = null, ILogger<TokenBroker>? logger = null)
    {
        _tokenManager = tokenManager;
        _logPipeline = logPipeline;
        _registry = registry ?? new AgentTokenRegistry();
        _logger = logger;
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
            var token = await _tokenManager.IssueTokenAsync($"agent:{config.AgentId}", TokenType.Agent, config.Capabilities, trustLevel, AgentDefaults.AgentTokenLifetime, [owner.Subject ?? "owner", $"agent:{config.AgentId}"], null, ct).ConfigureAwait(false);
            var validation = await _tokenManager.ValidateTokenAsync(token, ct).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            var result = new AgentTokenResult
            {
                Token = token,
                AgentId = config.AgentId,
                Capabilities = [.. config.Capabilities],
                IssuedAt = now,
                ExpiresAt = validation.ExpiresAt ?? now.Add(AgentDefaults.AgentTokenLifetime),
            };
            _registry.Upsert(new AgentTokenState(config.AgentId, token, validation.Jti, result.ExpiresAt, result.Capabilities));
            granted = true;
            return result;
        }
        finally
        {
            // Audit best-effort: a failing audit sink must never replace the original
            // outcome (success or thrown exception) of the token operation.
            await TryWriteAuditAsync("agent.token.issue", config.AgentId, granted).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<AgentTokenResult> RenewAgentTokenAsync(string agentId, string token, CancellationToken ct)
    {
        var granted = false;
        try
        {
            // Bind the renewal to the token this process actually issued for the agent:
            // accepting an arbitrary caller-supplied token would let any holder of a valid
            // token refresh a session it does not own.
            var known = _registry.Snapshot().FirstOrDefault(s => string.Equals(s.AgentId, agentId, StringComparison.OrdinalIgnoreCase));
            if (known is null || !string.Equals(known.Token, token, StringComparison.Ordinal))
            {
                throw new TokenValidationException($"Agent {agentId} has no matching registered token to renew.");
            }

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
                ExpiresAt = validation.ExpiresAt ?? now.Add(AgentDefaults.AgentTokenLifetime),
            };
            _registry.Upsert(new AgentTokenState(agentId, renewed, validation.Jti, result.ExpiresAt, result.Capabilities));
            granted = true;
            return result;
        }
        finally
        {
            await TryWriteAuditAsync("agent.token.renew", agentId, granted).ConfigureAwait(false);
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
            await TryWriteAuditAsync("agent.token.revoke", agentId, granted).ConfigureAwait(false);
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

    /// <summary>
    /// Writes an audit entry with a short timeout, swallowing failures. Used from finally
    /// blocks so that audit plumbing issues never mask the outcome (success or exception)
    /// of the token operation itself, and never block shutdown on the business token.
    /// </summary>
    private async Task TryWriteAuditAsync(string action, string agentId, bool granted)
    {
        try
        {
            using var auditCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await _logPipeline.ProcessAsync(new LogEntry
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
            }, auditCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Failed to write token audit for {Action} of {AgentId}.", action, agentId);
        }
        catch (OperationCanceledException)
        {
            // Audit timed out; the operation outcome is still delivered to the caller.
        }
    }
}
