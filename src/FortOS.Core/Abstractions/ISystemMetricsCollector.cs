namespace FortOS.Core;

/// <summary>Collects a point-in-time host and workload monitoring snapshot.</summary>
public interface ISystemMetricsCollector
{
    /// <summary>Collect all available metrics without failing the whole snapshot for optional subsystems.</summary>
    Task<SystemMetricsSnapshot> CollectAsync(CancellationToken ct);
}
