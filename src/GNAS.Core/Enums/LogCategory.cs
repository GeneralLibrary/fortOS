namespace GNAS.Core;

/// <summary>
/// 日志类别。
/// </summary>
public enum LogCategory
{
    /// <summary>
    /// 系统运行日志。
    /// </summary>
    System,
    /// <summary>
    /// 安全审计日志。
    /// </summary>
    Audit,
    /// <summary>
    /// 访问日志。
    /// </summary>
    Access,
    /// <summary>
    /// Agent 日志。
    /// </summary>
    Agent,
    /// <summary>
    /// 链路追踪日志。
    /// </summary>
    Trace,
    /// <summary>
    /// 指标日志。
    /// </summary>
    Metric,
}
