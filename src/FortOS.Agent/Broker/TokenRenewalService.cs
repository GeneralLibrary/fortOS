using System.Text.Json;
using FortOS.Agent.Infrastructure;
using FortOS.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FortOS.Agent.Broker;

/// <summary>
/// Publishes events and automatically renews Agent tokens before they expire.
/// </summary>
public sealed class TokenRenewalService : BackgroundService
{
    private readonly AgentTokenRegistry _registry;
    private readonly ITokenBroker _broker;
    private readonly IEventBus _eventBus;
    private readonly IServiceSupervisor _supervisor;
    private readonly ILogger<TokenRenewalService>? _logger;

    /// <summary>
    /// Initialize the token renewal background service.
    /// </summary>
    public TokenRenewalService(AgentTokenRegistry registry, ITokenBroker broker, IEventBus eventBus, IServiceSupervisor supervisor, ILogger<TokenRenewalService>? logger = null)
    {
        _registry = registry;
        _broker = broker;
        _eventBus = eventBus;
        _supervisor = supervisor;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(AgentDefaults.RenewalPollInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            await RenewExpiringTokensAsync(stoppingToken).ConfigureAwait(false);
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task RenewExpiringTokensAsync(CancellationToken ct)
    {
        // Drop already-expired entries first so the renewal loop below never keeps
        // retrying tokens that can no longer be renewed.
        _registry.PruneExpired();

        var threshold = DateTimeOffset.UtcNow.Add(AgentDefaults.RenewalLeadTime);
        foreach (var state in _registry.Snapshot().Where(s => s.ExpiresAt <= threshold))
        {
            try
            {
                await _eventBus.PublishAsync($"agent.{state.AgentId}.token.expiring", "agent.token.expiring", JsonSerializer.Serialize(new { state.AgentId, state.ExpiresAt }), ct).ConfigureAwait(false);
                var renewed = await _broker.RenewAgentTokenAsync(state.AgentId, state.Token, ct).ConfigureAwait(false);
                // Renewing revokes the old token, so write the new token back to the agent's .env and
                // recreate the container; otherwise NAS_TOKEN inside it stays the revoked old value and
                // the agent will lose connectivity before expiry (.env is written only at deploy time).
                await ApplyRenewedTokenAsync(state.AgentId, renewed.Token, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // One agent failing to renew (e.g. revoked or malformed token) must not stop
                // renewal for the remaining agents — log and move on instead of letting the
                // exception escape and kill the whole BackgroundService.
                _logger?.LogWarning(ex, "Failed to renew token for agent {AgentId}; skipping.", state.AgentId);
            }
        }
    }

    /// <summary>
    /// Writes the renewed token back to the agent's .env (atomic replace, keeping 600 perms)
    /// and recreates its container so the NAS_TOKEN inside matches the newly issued token.
    /// Both steps are best-effort: a failure in either only logs and never blocks later renewals.
    /// </summary>
    private async Task ApplyRenewedTokenAsync(string agentId, string token, CancellationToken ct)
    {
        try
        {
            await UpdateEnvTokenAsync(agentId, token, ct).ConfigureAwait(false);
            // RestartAsync does down + up -d internally: the container must be recreated;
            // docker restart does not re-read env_file, so the new NAS_TOKEN is not applied.
            await _supervisor.RestartAsync($"agent-{agentId}", ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Agent {AgentId} token was renewed but applying it to the container failed; the agent may lose connectivity until its container is recreated.", agentId);
        }
    }

    /// <summary>Replaces the NAS_TOKEN line in .env; silently skips if the file is missing (deployment not yet completed).</summary>
    private static async Task UpdateEnvTokenAsync(string agentId, string token, CancellationToken ct)
    {
        var envPath = Path.Combine(AgentPaths.AgentsRoot, agentId, ".env");
        if (!File.Exists(envPath))
        {
            return;
        }

        var lines = await File.ReadAllLinesAsync(envPath, ct).ConfigureAwait(false);
        var replaced = false;
        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index].StartsWith("NAS_TOKEN=", StringComparison.Ordinal))
            {
                lines[index] = "NAS_TOKEN=" + token;
                replaced = true;
            }
        }

        if (!replaced)
        {
            return;
        }

        // Atomic replace: write a temp file first (600 permissions, matching ComposeGenerator) then rename.
        var tempPath = envPath + ".tmp";
        await File.WriteAllLinesAsync(tempPath, lines, ct).ConfigureAwait(false);
        File.SetUnixFileMode(tempPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(tempPath, envPath, overwrite: true);
    }
}
