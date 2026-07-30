namespace GORT.Core;

/// <summary>Provides current and historical system monitoring data.</summary>
public interface ISystemMetricsService
{
    /// <summary>Return the latest snapshot, collecting one on demand before the background loop has run.</summary>
    Task<SystemMetricsSnapshot> GetCurrentAsync(CancellationToken ct);

    /// <summary>Query persisted scalar metrics.</summary>
    Task<IReadOnlyList<MetricData>> GetHistoryAsync(SystemMetricHistoryQuery query, CancellationToken ct);
}
