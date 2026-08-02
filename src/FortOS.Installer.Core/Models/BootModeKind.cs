namespace FortOS.Installer.Core.Models;

/// <summary>实际引导方式(替代 "uefi"/"bios" 魔法字符串)。</summary>
public enum BootModeKind
{
    Uefi,
    Bios,
}

/// <summary>BootModeKind 辅助。</summary>
public static class BootModeKindExtensions
{
    /// <summary>序列化用小写字符串(install-summary.json 的 BootMode 字段)。</summary>
    public static string ToLowerInvariant(this BootModeKind kind)
        => kind == BootModeKind.Uefi ? "uefi" : "bios";
}
