namespace GNAS.Core;

/// <summary>
/// 服务启动策略。
/// </summary>
public enum ServiceStartup
{
    /// <summary>
    /// 随系统自动启动。
    /// </summary>
    Automatic,
    /// <summary>
    /// 手动启动。
    /// </summary>
    Manual,
    /// <summary>
    /// 禁用启动。
    /// </summary>
    Disabled,
}
