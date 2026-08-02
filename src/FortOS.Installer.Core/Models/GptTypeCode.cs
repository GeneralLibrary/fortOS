namespace FortOS.Installer.Core.Models;

/// <summary>
/// GPT 分区类型码集中定义(sgdisk --typecode 使用)。避免
/// "ef00"/"8304" 等魔法字符串在模板/分区/格式化多处散落。
/// </summary>
public static class GptTypeCode
{
    /// <summary>EFI System Partition(ESP,FAT32)。</summary>
    public const string EfiSystem = "ef00";

    /// <summary>BIOS boot(grub 第二段,无需文件系统)。</summary>
    public const string BiosBoot = "ef02";

    /// <summary>Linux x86-64 root(/)。</summary>
    public const string LinuxX8664Root = "8304";

    /// <summary>Linux swap。</summary>
    public const string LinuxSwap = "8200";

    /// <summary>Linux filesystem(通用数据分区)。</summary>
    public const string LinuxFilesystem = "8300";
}
