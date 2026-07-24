namespace GNAS.Core;

/// <summary>
/// 健康状态。
/// </summary>
public enum HealthStatus
{
    /// <summary>
    /// 健康。
    /// </summary>
    Healthy,
    /// <summary>
    /// 降级运行。
    /// </summary>
    Degraded,
    /// <summary>
    /// 不健康。
    /// </summary>
    Unhealthy,
    /// <summary>
    /// 状态未知。
    /// </summary>
    Unknown,
}
