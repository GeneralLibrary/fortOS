namespace FortOS.Installer.Core.Models;

/// <summary>Root file system of the system disk.</summary>
public enum RootFileSystem
{
    /// <summary>ext4 (conservative default).</summary>
    Ext4,

    /// <summary>btrfs (snapshot capability, recommended default).</summary>
    Btrfs,
}

/// <summary>File system for a single data disk.</summary>
public enum DataFileSystem
{
    Ext4,
    Xfs,
    Btrfs,
}

/// <summary>Data disk layout mode. Single disk / mdadm RAID / LUKS encryption, or not configured for now.</summary>
public enum DataDiskMode
{
    /// <summary>Do not configure a data disk yet; initialized later by FortOS on first boot.</summary>
    None,

    /// <summary>Single disk, single partition, formatted with the specified file system.</summary>
    Single,

    /// <summary>mdadm RAID (whole-disk members, level 1/5/10).</summary>
    Raid,

    /// <summary>LUKS2-encrypted single disk (whole-disk container).</summary>
    Luks,
}

/// <summary>Network configuration during installation.</summary>
public enum NetworkMode
{
    /// <summary>DHCP (default).</summary>
    Dhcp,

    /// <summary>Static IP.</summary>
    Static,
}

/// <summary>Boot mode. Auto is determined by live environment detection.</summary>
public enum BootloaderMode
{
    Auto,
    Uefi,
    Bios,
}

/// <summary>Swap partition policy.</summary>
public enum SwapMode
{
    /// <summary>Default: equal to the memory size.</summary>
    Auto,

    /// <summary>Do not create a swap partition.</summary>
    Off,

    /// <summary>Fixed size (in conjunction with <see cref="InstallConfig.SwapSizeMiB"/>).</summary>
    Fixed,
}
