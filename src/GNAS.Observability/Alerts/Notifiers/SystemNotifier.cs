using GNAS.Core;
using Microsoft.Extensions.Logging;

namespace GNAS.Observability.Alerts.Notifiers;

/// <summary>Alert notifier that writes to system log.</summary>
public sealed class SystemNotifier : INotifier
{
    private readonly ILogPipeline _pipeline;

    /// <summary>Initialize system notifier.</summary>
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

    /// <inheritdoc />
    public Task NotifyResolvedAsync(ActiveAlert alert, AlertRule rule, MetricData metric, CancellationToken ct)
        => _pipeline.ProcessAsync(new LogEntry
        {
            Category = LogCategory.System,
            Level = LogLevel.Information,
            SourceComponent = "AlertEngine",
            SourceLayer = "Observability",
            Message = $"Alert recovered: {rule.Name}; {metric.MetricName}={metric.Value}",
            Properties = new Dictionary<string, object>
            {
                ["rule_id"] = rule.RuleId,
                ["alert_id"] = alert.AlertId,
                ["metric"] = metric.MetricName,
            }
        }, ct);
}
