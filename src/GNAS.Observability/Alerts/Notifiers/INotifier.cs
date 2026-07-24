using GNAS.Core;

namespace GNAS.Observability.Alerts.Notifiers;

/// <summary>告警通知器接口。</summary>
public interface INotifier
{
    /// <summary>发送活跃告警通知。</summary>
    Task NotifyAsync(ActiveAlert alert, AlertRule rule, CancellationToken ct);
}
