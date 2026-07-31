using System.Text.Json;
using FortOS.Core;
using Microsoft.Extensions.Hosting;

namespace FortOS.Agent.Broker;

/// <summary>
/// Publishes events and automatically renews Agent tokens before they expire.
/// </summary>
public sealed class TokenRenewalService : BackgroundService
{
    private readonly AgentTokenRegistry _registry;
    private readonly ITokenBroker _broker;
    private readonly IEventBus _eventBus;

    /// <summary>
    /// Initialize the token renewal background service.
    /// </summary>
    public TokenRenewalService(AgentTokenRegistry registry, ITokenBroker broker, IEventBus eventBus)
    {
        _registry = registry;
        _broker = broker;
        _eventBus = eventBus;
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
        var threshold = DateTimeOffset.UtcNow.AddHours(1);
        foreach (var state in _registry.Snapshot().Where(s => s.ExpiresAt <= threshold))
        {
            await _eventBus.PublishAsync($"agent.{state.AgentId}.token.expiring", "agent.token.expiring", JsonSerializer.Serialize(new { state.AgentId, state.ExpiresAt }), ct).ConfigureAwait(false);
            await _broker.RenewAgentTokenAsync(state.AgentId, state.Token, ct).ConfigureAwait(false);
        }
    }
}
