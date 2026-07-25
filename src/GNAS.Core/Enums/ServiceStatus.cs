namespace GNAS.Core;

/// <summary>
/// 服务运行状态。
/// </summary>
public enum ServiceStatus
{
    /// <summary>
    /// 已停止。
    /// </summary>
    Stopped,
    /// <summary>
    /// 启动中。
    /// </summary>
    Starting,
    /// <summary>
    /// 运行中。
    /// </summary>
    Running,
    /// <summary>
    /// 停止中。
    /// </summary>
    Stopping,
    /// <summary>
    /// 运行失败。
    /// </summary>
    Failed,
    /// <summary>
    /// 状态未知。
    /// </summary>
    Unknown,
}
