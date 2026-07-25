namespace GNAS.Core;

/// <summary>
/// 服务宿主类型。
/// </summary>
public enum ServiceType
{
    /// <summary>
    /// 原生操作系统进程。
    /// </summary>
    Native,
    /// <summary>
    /// 容器化服务。
    /// </summary>
    Container,
    /// <summary>
    /// .NET 进程内模块。
    /// </summary>
    Module,
    /// <summary>
    /// 由 systemd 管理的 Linux 系统服务。
    /// </summary>
    Systemd,
}
