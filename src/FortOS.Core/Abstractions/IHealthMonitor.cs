namespace FortOS.Core;

/// <summary>Health monitor interface.</summary>
public interface IHealthMonitor
{
    /// <summary>Register a health check.</summary>
    Task RegisterAsync(string serviceId, HealthCheckConfig config, CancellationToken ct);
    /// <summary>Unregister a health check.</summary>
    Task UnregisterAsync(string serviceId, CancellationToken ct);
    /// <summary>Get health status.</summary>
    Task<HealthStatus> GetStatusAsync(string serviceId, CancellationToken ct);
    /// <summary>Get recent check results.</summary>
    Task<IReadOnlyList<HealthCheckResult>> GetRecentResultsAsync(string serviceId, int limit, CancellationToken ct);
    /// <summary>Get latency percentiles.</summary>
    Task<IReadOnlyDictionary<double, TimeSpan>> GetLatencyPercentilesAsync(string serviceId, IReadOnlyList<double> percentiles, CancellationToken ct);
}
