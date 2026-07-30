using GORT.Core;
using Microsoft.Extensions.Hosting;

namespace GORT.Observability.Audit;

/// <summary>Daily audit chain integrity verification background service.</summary>
public sealed class VerificationBackgroundService : BackgroundService
{
    private readonly IAuditChain _auditChain;
    private readonly IEventBus _eventBus;

    /// <summary>Initialize audit chain verification background service.</summary>
    public VerificationBackgroundService(IAuditChain auditChain, IEventBus eventBus)
    {
        _auditChain = auditChain;
        _eventBus = eventBus;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(DelayUntilNextRun(), stoppingToken).ConfigureAwait(false);
            var result = await _auditChain.VerifyIntegrityAsync(null, null, stoppingToken).ConfigureAwait(false);
            if (!result.IsValid)
            {
                await _eventBus.PublishAsync("audit.chain.broken", "critical", System.Text.Json.JsonSerializer.Serialize(result), stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private static TimeSpan DelayUntilNextRun()
    {
        var now = DateTime.Now;
        var next = now.Date.AddHours(3);
        if (next <= now) next = next.AddDays(1);
        return next - now;
    }
}
