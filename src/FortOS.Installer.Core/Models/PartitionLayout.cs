namespace FortOS.Installer.Core.Models;

/// <summary>Partition file system type (used during the formatting phase).</summary>
public enum PartitionFs
{
    /// <summary>Do not format (e.g. BIOS boot partition).</summary>
    None,

    /// <summary>FAT32 (EFI System Partition).</summary>
    Vfat,

    /// <summary>ext4.</summary>
    Ext4,

    /// <summary>btrfs.</summary>
    Btrfs,

    /// <summary>xfs.</summary>
    Xfs,

    /// <summary>swap.</summary>
    Swap,
}

/// <summary>A single partition specification (part of a template).</summary>
public sealed record PartitionSpec
{
    /// <summary>Partition number (starting at 1).</summary>
    public required int Number { get; init; }

    /// <summary>Partition size (MiB); 0 means consume all remaining space.</summary>
    public long SizeMiB { get; init; }

    /// <summary>gdisk type code, e.g. <c>ef02</c> (BIOS boot), <c>ef00</c> (EFI System), <c>8304</c> (Linux x86-64 root), <c>8200</c> (swap).</summary>
    public required string TypeCode { get; init; }

    /// <summary>Partition name (GPT name).</summary>
    public string? Label { get; init; }

    /// <summary>File system used for formatting.</summary>
    public PartitionFs Fs { get; init; } = PartitionFs.None;
}

/// <summary>GPT layout template for the system disk. A fixed v1 template, no free-form layout (see design doc 1.3).</summary>
public static class PartitionTemplates
{
    /// <summary>BIOS boot partition size (MiB).</summary>
    public const long BiosBootMiB = 1;

    /// <summary>EFI System Partition size (MiB).</summary>
    public const long EfiMiB = 512;

    /// <summary>
    /// Generates the default system disk layout: p1 BIOS boot → p2 EFI → root partition (remaining).
    /// When swap is needed, the swap partition is created before the root partition (partition sizes are
    /// allocated from the disk start in creation order, so the "tail partition" must be the last one),
    /// and the root partition is still the tail.
    /// The partition numbering differs from the p4 swap position in design doc 5.2; functionally equivalent.
    /// </summary>
    /// <param name="swapMiB">Swap size (MiB); 0 or a negative value means no swap is created.</param>
    public static IReadOnlyList<PartitionSpec> SystemDefault(long swapMiB)
    {
        var partitions = new List<PartitionSpec>
        {
            new() { Number = 1, SizeMiB = BiosBootMiB, TypeCode = GptTypeCode.BiosBoot, Label = "BIOS boot", Fs = PartitionFs.None },
            new() { Number = 2, SizeMiB = EfiMiB, TypeCode = GptTypeCode.EfiSystem, Label = "EFI System", Fs = PartitionFs.Vfat },
        };

        if (swapMiB > 0)
        {
            partitions.Add(new() { Number = 3, SizeMiB = swapMiB, TypeCode = GptTypeCode.LinuxSwap, Label = "swap", Fs = PartitionFs.Swap });
        }

        var rootNumber = partitions.Count + 1;
        // The root partition Fs is determined by FormatStep based on the configuration (TypeCode=8304 branch); this is only a placeholder.
        partitions.Add(new() { Number = rootNumber, SizeMiB = 0, TypeCode = GptTypeCode.LinuxX8664Root, Label = "FortOS root", Fs = PartitionFs.Ext4 });
        return partitions;
    }
}
