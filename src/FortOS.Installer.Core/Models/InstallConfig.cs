using FortOS.Installer.Core.Models;

namespace FortOS.Installer.Core.Models;

/// <summary>
/// A complete installation configuration. The CLI (same source as GUI) generates it via
/// <c>install.yaml</c> or interactive wizard; it is the shared contract of the engine's three frontends.
/// </summary>
public sealed class InstallConfig
{
    /// <summary>System disk device path, e.g. <c>/dev/sda</c>.</summary>
    public required string SystemDisk { get; init; }

    /// <summary>Root file system of the system disk.</summary>
    public RootFileSystem RootFs { get; init; } = RootFileSystem.Btrfs;

    /// <summary>Swap partition policy.</summary>
    public SwapMode SwapMode { get; init; } = SwapMode.Auto;

    /// <summary>Fixed size of the swap partition (MiB), only applies when <see cref="SwapMode.Fixed"/>.</summary>
    public long? SwapSizeMiB { get; init; }

    /// <summary>Data disk layout (can be skipped).</summary>
    public DataDiskConfig Data { get; init; } = new() { Mode = DataDiskMode.None };

    /// <summary>Network configuration.</summary>
    public NetworkConfig Network { get; init; } = new();

    /// <summary>Administrator account.</summary>
    public AccountConfig Account { get; init; } = new();

    /// <summary>Language and timezone.</summary>
    public LocaleConfig Locale { get; init; } = new();

    /// <summary>Boot mode.</summary>
    public BootloaderMode Bootloader { get; init; } = BootloaderMode.Auto;

    /// <summary>
    /// System copy source. Defaults to <c>/</c> (copies the running live rootfs, excluding virtual
    /// file systems); can also point to a mounted squashfs directory.
    /// </summary>
    public string SourcePath { get; init; } = "/";
}

/// <summary>Data disk configuration.</summary>
public sealed class DataDiskConfig
{
    /// <summary>Layout mode.</summary>
    public DataDiskMode Mode { get; init; } = DataDiskMode.None;

    /// <summary>Data disk device path (required for <see cref="DataDiskMode.Single"/> / <see cref="DataDiskMode.Luks"/>).</summary>
    public string? Disk { get; init; }

    /// <summary>Data disk file system.</summary>
    public DataFileSystem FileSystem { get; init; } = DataFileSystem.Btrfs;

    /// <summary>Data partition label.</summary>
    public string Label { get; init; } = "FORTOS_DATA";

    /// <summary>RAID level (1/5/10), applies when <see cref="DataDiskMode.Raid"/>.</summary>
    public int RaidLevel { get; init; } = 1;

    /// <summary>RAID member disks (required for <see cref="DataDiskMode.Raid"/>; whole disks participate).</summary>
    public IReadOnlyList<string> RaidDisks { get; init; } = [];

    /// <summary>RAID device name (default md127).</summary>
    public string RaidDeviceName { get; init; } = "md127";

    /// <summary>LUKS passphrase (required for <see cref="DataDiskMode.Luks"/>; passed to cryptsetup via stdin).</summary>
    public string LuksPassphrase { get; init; } = string.Empty;

    /// <summary>LUKS mapper name (default fortos-data).</summary>
    public string LuksMapperName { get; init; } = "fortos-data";
}

/// <summary>Network configuration.</summary>
public sealed class NetworkConfig
{
    /// <summary>DHCP or static.</summary>
    public NetworkMode Mode { get; init; } = NetworkMode.Dhcp;

    /// <summary>Hostname.</summary>
    public string Hostname { get; init; } = "fortos";

    /// <summary>Static address (CIDR), e.g. <c>192.168.1.10/24</c>.</summary>
    public string? Address { get; init; }

    /// <summary>Gateway.</summary>
    public string? Gateway { get; init; }

    /// <summary>DNS server list.</summary>
    public IReadOnlyList<string> Dns { get; init; } = [];
}

/// <summary>Administrator account configuration.</summary>
public sealed class AccountConfig
{
    /// <summary>Administrator username.</summary>
    public string Username { get; init; } = "admin";

    /// <summary>Password (plaintext; used by the headless/automation path, the GUI passes it directly in memory).</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>Optional SSH public key (multiple lines separated by newlines).</summary>
    public string SshPublicKey { get; init; } = string.Empty;

    /// <summary>Timezone, e.g. <c>Asia/Shanghai</c>.</summary>
    public string Timezone { get; init; } = "UTC";
}

/// <summary>Language and keyboard configuration.</summary>
public sealed class LocaleConfig
{
    /// <summary>System locale, e.g. <c>en_US.UTF-8</c>.</summary>
    public string Language { get; init; } = "en_US.UTF-8";

    /// <summary>Keyboard layout, e.g. <c>us</c>.</summary>
    public string Keyboard { get; init; } = "us";
}
