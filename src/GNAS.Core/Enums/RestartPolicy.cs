namespace GNAS.Core;

/// <summary>
/// 服务重启策略。
/// </summary>
public enum RestartPolicy
{
    /// <summary>
    /// 总是重启。
    /// </summary>
    Always,
    /// <summary>
    /// 失败时重启。
    /// </summary>
    OnFailure,
    /// <summary>
    /// 从不自动重启。
    /// </summary>
    Never,
    /// <summary>
    /// 按指数退避重启。
    /// </summary>
    ExponentialBackoff,
}
