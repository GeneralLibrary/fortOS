using GNAS.Core;

namespace GNAS.Observability.Alerts.Notifiers;

/// <summary>Alert notifier interface.</summary>
public interface INotifier
{
    /// <summary>Send active alert notification.</summary>
    Task NotifyAsync(ActiveAlert alert, AlertRule rule, CancellationToken ct);
}
