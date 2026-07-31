using FortOS.Core;

namespace FortOS.Observability.Alerts.Notifiers;

/// <summary>Alert notifier interface.</summary>
public interface INotifier
{
    /// <summary>Send active alert notification.</summary>
    Task NotifyAsync(ActiveAlert alert, AlertRule rule, CancellationToken ct);

    /// <summary>Send recovery notification after a metric returns to its healthy range.</summary>
    Task NotifyResolvedAsync(ActiveAlert alert, AlertRule rule, MetricData metric, CancellationToken ct)
        => Task.CompletedTask;
}
