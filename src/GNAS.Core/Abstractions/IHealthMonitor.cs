namespace GNAS.Core;

/// <summary>健康监控接口。</summary>
public interface IHealthMonitor
{
    /// <summary>注册健康检查。</summary>
    Task RegisterAsync(string serviceId, HealthCheckConfig config, CancellationToken ct);
    /// <summary>注销健康检查。</summary>
    Task UnregisterAsync(string serviceId, CancellationToken ct);
    /// <summary>获取健康状态。</summary>
    Task<HealthStatus> GetStatusAsync(string serviceId, CancellationToken ct);
    /// <summary>获取最近检查结果。</summary>
    Task<IReadOnlyList<HealthCheckResult>> GetRecentResultsAsync(string serviceId, int limit, CancellationToken ct);
    /// <summary>获取延迟百分位。</summary>
    Task<IReadOnlyDictionary<double, TimeSpan>> GetLatencyPercentilesAsync(string serviceId, IReadOnlyList<double> percentiles, CancellationToken ct);
}
