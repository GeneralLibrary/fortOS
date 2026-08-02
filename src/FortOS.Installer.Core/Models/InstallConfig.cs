using FortOS.Installer.Core.Models;

namespace FortOS.Installer.Core.Models;

/// <summary>
/// 一份完整的安装配置。CLI(GUI 同源)通过 <c>install.yaml</c> 或向导交互生成,
/// 是引擎三个前端的共享契约。
/// </summary>
public sealed class InstallConfig
{
    /// <summary>系统盘设备路径,如 <c>/dev/sda</c>。</summary>
    public required string SystemDisk { get; init; }

    /// <summary>系统盘根文件系统。</summary>
    public RootFileSystem RootFs { get; init; } = RootFileSystem.Btrfs;

    /// <summary>交换分区策略。</summary>
    public SwapMode SwapMode { get; init; } = SwapMode.Auto;

    /// <summary>交换分区固定大小(MiB),仅 <see cref="SwapMode.Fixed"/> 时生效。</summary>
    public long? SwapSizeMiB { get; init; }

    /// <summary>数据盘布局(可跳过)。</summary>
    public DataDiskConfig Data { get; init; } = new() { Mode = DataDiskMode.None };

    /// <summary>网络配置。</summary>
    public NetworkConfig Network { get; init; } = new();

    /// <summary>管理员账户。</summary>
    public AccountConfig Account { get; init; } = new();

    /// <summary>语言与时区。</summary>
    public LocaleConfig Locale { get; init; } = new();

    /// <summary>引导方式。</summary>
    public BootloaderMode Bootloader { get; init; } = BootloaderMode.Auto;

    /// <summary>
    /// 系统复制源。默认 <c>/</c>(复制运行中的 live rootfs,排除虚拟文件系统);
    /// 也可指向已挂载的 squashfs 目录。
    /// </summary>
    public string SourcePath { get; init; } = "/";
}

/// <summary>数据盘配置。</summary>
public sealed class DataDiskConfig
{
    /// <summary>布局模式。</summary>
    public DataDiskMode Mode { get; init; } = DataDiskMode.None;

    /// <summary>数据盘设备路径(<see cref="DataDiskMode.Single"/> / <see cref="DataDiskMode.Luks"/> 时必填)。</summary>
    public string? Disk { get; init; }

    /// <summary>数据盘文件系统。</summary>
    public DataFileSystem FileSystem { get; init; } = DataFileSystem.Btrfs;

    /// <summary>数据分区卷标。</summary>
    public string Label { get; init; } = "FORTOS_DATA";

    /// <summary>RAID 级别(1/5/10),<see cref="DataDiskMode.Raid"/> 时生效。</summary>
    public int RaidLevel { get; init; } = 1;

    /// <summary>RAID 成员盘(<see cref="DataDiskMode.Raid"/> 时必填,整盘参与)。</summary>
    public IReadOnlyList<string> RaidDisks { get; init; } = [];

    /// <summary>RAID 设备名(默认 md127)。</summary>
    public string RaidDeviceName { get; init; } = "md127";

    /// <summary>LUKS 口令(<see cref="DataDiskMode.Luks"/> 时必填;经 stdin 传给 cryptsetup)。</summary>
    public string LuksPassphrase { get; init; } = string.Empty;

    /// <summary>LUKS 映射名(默认 fortos-data)。</summary>
    public string LuksMapperName { get; init; } = "fortos-data";
}

/// <summary>网络配置。</summary>
public sealed class NetworkConfig
{
    /// <summary>DHCP 或静态。</summary>
    public NetworkMode Mode { get; init; } = NetworkMode.Dhcp;

    /// <summary>主机名。</summary>
    public string Hostname { get; init; } = "fortos";

    /// <summary>静态地址(CIDR),如 <c>192.168.1.10/24</c>。</summary>
    public string? Address { get; init; }

    /// <summary>网关。</summary>
    public string? Gateway { get; init; }

    /// <summary>DNS 服务器列表。</summary>
    public IReadOnlyList<string> Dns { get; init; } = [];
}

/// <summary>管理员账户配置。</summary>
public sealed class AccountConfig
{
    /// <summary>管理员用户名。</summary>
    public string Username { get; init; } = "admin";

    /// <summary>密码(明文;headless/自动化路径使用,GUI 在内存中直接传递)。</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>可选 SSH 公钥(多行以换行分隔)。</summary>
    public string SshPublicKey { get; init; } = string.Empty;

    /// <summary>时区,如 <c>Asia/Shanghai</c>。</summary>
    public string Timezone { get; init; } = "UTC";
}

/// <summary>语言与键盘配置。</summary>
public sealed class LocaleConfig
{
    /// <summary>系统 locale,如 <c>en_US.UTF-8</c>。</summary>
    public string Language { get; init; } = "en_US.UTF-8";

    /// <summary>键盘布局,如 <c>us</c>。</summary>
    public string Keyboard { get; init; } = "us";
}
