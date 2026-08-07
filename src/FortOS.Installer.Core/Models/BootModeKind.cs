namespace FortOS.Installer.Core.Models;

/// <summary>Actual boot mode (replaces the "uefi"/"bios" magic strings).</summary>
public enum BootModeKind
{
    Uefi,
    Bios,
}

/// <summary>BootModeKind helpers.</summary>
public static class BootModeKindExtensions
{
    /// <summary>Lowercase string for serialization (the BootMode field of install-summary.json).</summary>
    public static string ToLowerInvariant(this BootModeKind kind)
        => kind == BootModeKind.Uefi ? "uefi" : "bios";
}
