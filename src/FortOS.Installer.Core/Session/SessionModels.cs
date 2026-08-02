namespace FortOS.Installer.Core.Session;

/// <summary>
/// 安装会话状态机阶段(设计稿 5.1)。确认(Confirm)由前端在调用引擎前完成,
/// 引擎内的阶段为确认后的顺序执行区。
/// </summary>
public enum InstallerPhase
{
    /// <summary>空闲/未开始。</summary>
    Idle,

    /// <summary>收集环境信息(检测引导方式、校验磁盘)。</summary>
    CollectInfo,

    /// <summary>确认页(前端语义,引擎不执行)。</summary>
    Confirm,

    /// <summary>磁盘分区。</summary>
    Partitioning,

    /// <summary>文件系统格式化与挂载。</summary>
    Formatting,

    /// <summary>系统复制(rsync live rootfs → 目标)。</summary>
    Copying,

    /// <summary>chroot 目标系统配置。</summary>
    Configuring,

    /// <summary>引导安装。</summary>
    Bootloader,

    /// <summary>收尾(卸载、写摘要)。</summary>
    Finalize,

    /// <summary>安装完成。</summary>
    Done,

    /// <summary>失败(可重试/重启重装)。</summary>
    Failed,
}

/// <summary>一条安装日志(内存环形缓冲 + 落盘)。</summary>
public sealed record InstallLogEntry(DateTimeOffset Timestamp, string Level, string Message);

/// <summary>步骤级进度(用于 UI 进度条)。</summary>
public sealed record InstallStepProgress(string Step, double Percent, string Message);

/// <summary>安装结果。</summary>
public sealed class InstallResult
{
    public required bool Success { get; init; }

    /// <summary>失败的步骤名;为 null 表示失败发生在任何步骤之前(如 CollectInfo 校验)。</summary>
    public string? FailedStep { get; init; }

    public string? ErrorMessage { get; init; }

    /// <summary>安装摘要(最终落盘 /etc/fortos/install-summary.json)。</summary>
    public Models.InstallSummary? Summary { get; init; }
}
