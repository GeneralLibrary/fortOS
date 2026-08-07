using System.Text.RegularExpressions;
using FortOS.Installer.Core.Models;
using FortOS.Installer.Core.Session;
using FortOS.Installer.Core.Tools;

namespace FortOS.Installer.Core.Steps;

/// <summary>
/// Bootloader installation step (design doc 5.5): install GRUB according to the detected boot mode and generate its configuration.
/// </summary>
public sealed class BootloaderStep : IInstallStep
{
    /// <summary>grub.cfg line that boots a kernel (linux / linux16 / linuxefi); must include the kernel path.</summary>
    private static readonly Regex LinuxEntryLine = new(
        @"^\s*linux(?:16|efi)?\s+\S+", RegexOptions.Compiled);

    /// <summary>grub.cfg line that loads initramfs (initrd / initrdefi); must include the image path.</summary>
    private static readonly Regex InitrdEntryLine = new(
        @"^\s*initrd(?:efi)?\s+\S+", RegexOptions.Compiled);

    private readonly GrubTool _grub;
    private readonly ChrootRunner _chroot;

    public BootloaderStep(GrubTool grub, ChrootRunner chroot)
    {
        _grub = grub;
        _chroot = chroot;
    }

    public string Name => "Bootloader";

    public InstallerPhase Phase => InstallerPhase.Bootloader;

    public async Task ExecuteAsync(InstallContext context, CancellationToken ct)
    {
        var target = context.TargetMount;
        switch (context.BootMode)
        {
            case BootModeKind.Uefi:
                await _grub.InstallUefiAsync(target, $"{target}/boot/efi", ct).ConfigureAwait(false);
                break;
            case BootModeKind.Bios:
                await _grub.InstallBiosAsync(target, context.Config.SystemDisk, ct).ConfigureAwait(false);
                break;
            default:
                throw new Exceptions.StepException(Name, $"Unknown boot mode: {context.BootMode}");
        }

        // Bind mounts are required (grub-mkconfig runs inside the chroot). ChrootStep
        // has already bound them; calling again is idempotent (repeated mount --bind
        // failures are ignored), which guarantees the chroot environment is ready in
        // retry scenarios (where cleanup after failure has unmounted them).
        await _chroot.BindMountsAsync(target, ct).ConfigureAwait(false);
        await _grub.MakeConfigAsync(target, ct).ConfigureAwait(false);

        // grub-mkconfig probes silently inside the chroot; if it cannot find a
        // kernel/initrd or fails to probe the root device, it emits a grub.cfg
        // missing linux/initrd/root= **without reporting an error**, and the install
        // reports "success" while the system crashes on reboot (VFS: Unable to mount
        // root fs on unknown-block(0,0)). The generated file must be validated; on
        // failure the install aborts with diagnostic information.
        var grubCfg = await File.ReadAllTextAsync(
            Path.Combine(target, "boot/grub/grub.cfg"), ct).ConfigureAwait(false);
        ValidateGrubConfig(grubCfg);
    }

    /// <summary>
    /// Validates that the grub.cfg produced by grub-mkconfig has the bootable
    /// essentials: at least one menuentry, a linux line carrying the root=
    /// parameter, and an initrd line. Pure function, unit-testable.
    /// </summary>
    internal static void ValidateGrubConfig(string grubConfig)
    {
        if (string.IsNullOrWhiteSpace(grubConfig))
        {
            throw new Exceptions.StepException(
                "Bootloader",
                "grub-mkconfig produced an empty grub.cfg — the installed system has no boot menu.");
        }

        var lines = grubConfig.Split('\n');
        var hasEntry = lines.Any(l => l.TrimStart().StartsWith("menuentry ", StringComparison.Ordinal));
        if (!hasEntry)
        {
            throw new Exceptions.StepException(
                "Bootloader",
                "grub.cfg contains no menuentry — grub-mkconfig failed to enumerate installed kernels.");
        }

        // Only actual kernel entries are counted (path contains vmlinuz);
        // non-kernel lines such as memtest86+'s "linux16 /boot/memtest86+.bin"
        // do not participate in validation, avoiding false positives.
        var linuxLines = lines
            .Where(l => LinuxEntryLine.IsMatch(l) && l.Contains("/boot/vmlinuz", StringComparison.Ordinal))
            .ToList();
        if (linuxLines.Count == 0)
        {
            throw new Exceptions.StepException(
                "Bootloader",
                "grub.cfg contains no linux kernel lines — /boot has no installed kernel image.");
        }
        // grub-mkconfig uses the same root probe result for every kernel entry, so
        // all kernel lines must carry root=; requiring it per line prevents a single
        // missing entry from being passed as a bootable configuration.
        if (linuxLines.Any(l => !l.Contains("root=", StringComparison.Ordinal)))
        {
            throw new Exceptions.StepException(
                "Bootloader",
                "a grub.cfg kernel line lacks the root= parameter — the kernel cannot locate the root filesystem (boot fails with 'VFS: Unable to mount root fs on unknown-block(0,0)').");
        }

        var hasInitrd = lines.Any(l => InitrdEntryLine.IsMatch(l));
        if (!hasInitrd)
        {
            throw new Exceptions.StepException(
                "Bootloader",
                "grub.cfg contains no initrd line — no initramfs was registered for the kernel. Run 'update-initramfs -u' inside the target system (or fix the copy step) and re-run grub-mkconfig.");
        }
    }
}
