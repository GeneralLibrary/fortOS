using System.Text.Json;
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
    private readonly ILogger<TokenRenewalService>? _logger;

    /// <summary>
    /// Initialize the token renewal background service.
    /// </summary>
    public TokenRenewalService(AgentTokenRegistry registry, ITokenBroker broker, IEventBus eventBus, ILogger<TokenRenewalService>? logger = null)
    {
        _registry = registry;
        _broker = broker;
        _eventBus = eventBus;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
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

        var threshold = DateTimeOffset.UtcNow.AddHours(1);
        foreach (var state in _registry.Snapshot().Where(s => s.ExpiresAt <= threshold))
        {
            try
            {
                await _eventBus.PublishAsync($"agent.{state.AgentId}.token.expiring", "agent.token.expiring", JsonSerializer.Serialize(new { state.AgentId, state.ExpiresAt }), ct).ConfigureAwait(false);
                await _broker.RenewAgentTokenAsync(state.AgentId, state.Token, ct).ConfigureAwait(false);
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
}
