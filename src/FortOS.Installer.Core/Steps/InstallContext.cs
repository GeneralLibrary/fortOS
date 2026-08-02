using FortOS.Installer.Core.Models;

namespace FortOS.Installer.Core.Steps;

/// <summary>
/// 步骤间共享的安装上下文:目标盘、分区设备、UUID、引导方式、摘要。
/// 由会话在 CollectInfo 阶段初始化,各步骤顺序填充。
/// </summary>
public sealed class InstallContext
{
    /// <summary>用户配置。</summary>
    public required InstallConfig Config { get; init; }

    /// <summary>目标 rootfs 挂载点。</summary>
    public string TargetMount { get; init; } = "/target";

    /// <summary>复制源(live rootfs 或 squashfs 挂载点)。</summary>
    public string SourcePath { get; init; } = "/";

    /// <summary>实际引导方式(由 PartitionStep 检测,供 Format/Bootloader 使用)。</summary>
    public BootModeKind? BootMode { get; set; }

    /// <summary>系统盘分区号 → 设备路径(如 2 → /dev/sda2)。</summary>
    public Dictionary<int, string> SystemPartitionDevices { get; } = [];

    /// <summary>系统盘分区规格(由 PartitionStep 生成,供 FormatStep 使用)。</summary>
    public List<PartitionSpec> SystemPartitions { get; } = [];

    /// <summary>数据盘设备(单盘分区 / RAID 设备 / LUKS mapper)。</summary>
    public string? DataDevice { get; set; }

    /// <summary>数据盘源设备(LUKS 容器所在盘,用于读取容器 UUID 写 crypttab)。</summary>
    public string? DataSourceDevice { get; set; }

    /// <summary>角色 → UUID:<c>root</c>/<c>efi</c>/<c>swap</c>/<c>data</c>。</summary>
    public Dictionary<string, string> Uuids { get; } = [];

    /// <summary>安装摘要(逐步填充)。</summary>
    public InstallSummary Summary { get; } = new();
}
