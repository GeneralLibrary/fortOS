namespace FortOS.Installer.Core.Models;

/// <summary>系统盘根文件系统。</summary>
public enum RootFileSystem
{
    /// <summary>ext4(默认保守选择)。</summary>
    Ext4,

    /// <summary>btrfs(快照能力,推荐默认)。</summary>
    Btrfs,
}

/// <summary>数据盘单盘文件系统。</summary>
public enum DataFileSystem
{
    Ext4,
    Xfs,
    Btrfs,
}

/// <summary>数据盘布局模式。单盘 / mdadm RAID / LUKS 加密,或暂不配置。</summary>
public enum DataDiskMode
{
    /// <summary>暂不配置数据盘,装后由 FortOS 引导初始化。</summary>
    None,

    /// <summary>单盘单分区,指定文件系统格式化。</summary>
    Single,

    /// <summary>mdadm RAID(整盘成员,level 1/5/10)。</summary>
    Raid,

    /// <summary>LUKS2 加密单盘(整盘容器)。</summary>
    Luks,
}

/// <summary>安装期网络配置。</summary>
public enum NetworkMode
{
    /// <summary>DHCP(默认)。</summary>
    Dhcp,

    /// <summary>静态 IP。</summary>
    Static,
}

/// <summary>引导方式。Auto 由 live 环境检测决定。</summary>
public enum BootloaderMode
{
    Auto,
    Uefi,
    Bios,
}

/// <summary>交换分区策略。</summary>
public enum SwapMode
{
    /// <summary>默认:等于内存大小。</summary>
    Auto,

    /// <summary>不创建交换分区。</summary>
    Off,

    /// <summary>固定大小(配合 <see cref="InstallConfig.SwapSizeMiB"/>)。</summary>
    Fixed,
}
