using GNAS.Core;
using Microsoft.Extensions.Logging;

namespace GNAS.Observability.Alerts.Notifiers;

/// <summary>写入系统日志的告警通知器。</summary>
public sealed class SystemNotifier : INotifier
{
    private readonly ILogPipeline _pipeline;

    /// <summary>初始化系统通知器。</summary>
    public SystemNotifier(ILogPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    /// <inheritdoc />
    public Task NotifyAsync(ActiveAlert alert, AlertRule rule, CancellationToken ct)
    {
        var level = alert.Severity.ToLowerInvariant() switch
        {
            "critical" => LogLevel.Critical,
            "warning" => LogLevel.Warning,
            _ => LogLevel.Information
        };
        return _pipeline.ProcessAsync(new LogEntry
        {
            Category = LogCategory.System,
            Level = level,
            SourceComponent = "AlertEngine",
            SourceLayer = "Observability",
            Message = alert.Message,
            Properties = new Dictionary<string, object> { ["rule_id"] = rule.RuleId, ["alert_id"] = alert.AlertId }
        }, ct);
    }
}
