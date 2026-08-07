namespace FortOS.Installer.Core.Models;

/// <summary>
/// Central definitions of GPT partition type codes (used by sgdisk --typecode). Avoids
/// magic strings such as "ef00"/"8304" being scattered across templates/partitions/formatting.
/// </summary>
public static class GptTypeCode
{
    /// <summary>EFI System Partition (ESP, FAT32).</summary>
    public const string EfiSystem = "ef00";

    /// <summary>BIOS boot (second stage of grub, no file system needed).</summary>
    public const string BiosBoot = "ef02";

    /// <summary>Linux x86-64 root (/).</summary>
    public const string LinuxX8664Root = "8304";

    /// <summary>Linux swap.</summary>
    public const string LinuxSwap = "8200";

    /// <summary>Linux filesystem (generic data partition).</summary>
    public const string LinuxFilesystem = "8300";
}
