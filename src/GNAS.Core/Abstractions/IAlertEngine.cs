namespace GNAS.Core;

/// <summary>告警引擎接口。</summary>
public interface IAlertEngine
{
    /// <summary>加载告警规则。</summary>
    Task LoadRulesAsync(CancellationToken ct);
    /// <summary>评估事件。</summary>
    Task EvaluateEventAsync(EventEnvelope envelope, CancellationToken ct);
    /// <summary>评估指标。</summary>
    Task EvaluateMetricAsync(MetricData metric, CancellationToken ct);
    /// <summary>列出活跃告警。</summary>
    Task<IReadOnlyList<ActiveAlert>> ListActiveAlertsAsync(CancellationToken ct);
    /// <summary>列出规则。</summary>
    Task<IReadOnlyList<AlertRule>> ListRulesAsync(CancellationToken ct);
    /// <summary>添加规则。</summary>
    Task AddRuleAsync(AlertRule rule, CancellationToken ct);
}
