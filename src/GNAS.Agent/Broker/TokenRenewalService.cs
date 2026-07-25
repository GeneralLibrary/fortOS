using System.Text.Json;
using GNAS.Core;
using Microsoft.Extensions.Hosting;

namespace GNAS.Agent.Broker;

/// <summary>
/// 在 Agent 令牌接近过期前发布事件并自动续期。
/// </summary>
public sealed class TokenRenewalService : BackgroundService
{
    private readonly AgentTokenRegistry _registry;
    private readonly ITokenBroker _broker;
    private readonly IEventBus _eventBus;

    /// <summary>
    /// 初始化令牌续期后台服务。
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
