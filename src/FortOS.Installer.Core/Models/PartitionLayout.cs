namespace FortOS.Installer.Core.Models;

/// <summary>分区文件系统类型(格式化阶段使用)。</summary>
public enum PartitionFs
{
    /// <summary>不格式化(如 BIOS boot 分区)。</summary>
    None,

    /// <summary>FAT32(EFI System Partition)。</summary>
    Vfat,

    /// <summary>ext4。</summary>
    Ext4,

    /// <summary>btrfs。</summary>
    Btrfs,

    /// <summary>xfs。</summary>
    Xfs,

    /// <summary>swap。</summary>
    Swap,
}

/// <summary>单个分区规格(模板的一部分)。</summary>
public sealed record PartitionSpec
{
    /// <summary>分区号(1 起)。</summary>
    public required int Number { get; init; }

    /// <summary>分区大小(MiB);0 表示消耗全部剩余空间。</summary>
    public long SizeMiB { get; init; }

    /// <summary>gdisk 类型码,如 <c>ef02</c>(BIOS boot)、<c>ef00</c>(EFI System)、<c>8304</c>(Linux x86-64 root)、<c>8200</c>(swap)。</summary>
    public required string TypeCode { get; init; }

    /// <summary>分区名(GPT name)。</summary>
    public string? Label { get; init; }

    /// <summary>格式化文件系统。</summary>
    public PartitionFs Fs { get; init; } = PartitionFs.None;
}

/// <summary>系统盘 GPT 布局模板。v1 固定模板,不做自由布图(见设计稿 1.3)。</summary>
public static class PartitionTemplates
{
    /// <summary>BIOS boot 分区大小(MiB)。</summary>
    public const long BiosBootMiB = 1;

    /// <summary>EFI System Partition 大小(MiB)。</summary>
    public const long EfiMiB = 512;

    /// <summary>
    /// 生成系统盘默认布局:p1 BIOS boot → p2 EFI → 根分区(剩余)。
    /// 需要 swap 时 swap 分区先于根分区创建(分区大小按创建顺序从盘头分配,
    /// 「收尾分区」必须是最后一个),根分区依旧收尾。
    /// 分区号与设计稿 5.2 的 p4 swap 位置不同,功能等价。
    /// </summary>
    /// <param name="swapMiB">swap 大小(MiB);0 或负值表示不创建 swap。</param>
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
        // 根分区 Fs 由 FormatStep 按配置决定(TypeCode=8304 分支),此处仅占位。
        partitions.Add(new() { Number = rootNumber, SizeMiB = 0, TypeCode = GptTypeCode.LinuxX8664Root, Label = "FortOS root", Fs = PartitionFs.Ext4 });
        return partitions;
    }
}
