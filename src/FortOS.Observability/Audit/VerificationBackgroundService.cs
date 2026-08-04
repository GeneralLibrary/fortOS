using FortOS.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FortOS.Observability.Audit;

/// <summary>Daily audit chain integrity verification background service.</summary>
public sealed class VerificationBackgroundService : BackgroundService
{
    private readonly IAuditChain _auditChain;
    private readonly IEventBus _eventBus;
    private readonly ILogger<VerificationBackgroundService> _logger;

    /// <summary>Initialize audit chain verification background service.</summary>
    public VerificationBackgroundService(IAuditChain auditChain, IEventBus eventBus, ILogger<VerificationBackgroundService> logger)
    {
        _auditChain = auditChain;
        _eventBus = eventBus;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(DelayUntilNextRun(), stoppingToken).ConfigureAwait(false);
            try
            {
                var result = await _auditChain.VerifyIntegrityAsync(null, null, stoppingToken).ConfigureAwait(false);
                if (!result.IsValid)
                {
                    await _eventBus.PublishAsync("audit.chain.broken", "critical", System.Text.Json.JsonSerializer.Serialize(result), stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A transient verification failure (DB lock, IO error) must not kill this service:
                // chain corruption would then never be detected again. Log and retry on the next
                // scheduled run.
                _logger.LogError(ex, "Audit chain verification failed; will retry on the next scheduled run.");
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
