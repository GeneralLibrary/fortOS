namespace FortOS.Core;

/// <summary>Alert engine interface.</summary>
public interface IAlertEngine
{
    /// <summary>Load alert rules.</summary>
    Task LoadRulesAsync(CancellationToken ct);
    /// <summary>Evaluate an event.</summary>
    Task EvaluateEventAsync(EventEnvelope envelope, CancellationToken ct);
    /// <summary>Evaluate a metric.</summary>
    Task EvaluateMetricAsync(MetricData metric, CancellationToken ct);
    /// <summary>List active alerts.</summary>
    Task<IReadOnlyList<ActiveAlert>> ListActiveAlertsAsync(CancellationToken ct);
    /// <summary>List rules.</summary>
    Task<IReadOnlyList<AlertRule>> ListRulesAsync(CancellationToken ct);
    /// <summary>Add a rule.</summary>
    Task AddRuleAsync(AlertRule rule, CancellationToken ct);
}
