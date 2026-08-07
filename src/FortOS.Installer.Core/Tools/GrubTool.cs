namespace FortOS.Installer.Core.Tools;

/// <summary>
/// <c>grub-install</c> / <c>grub-mkconfig</c> adapter: bootloader installation (design draft 5.5).
/// </summary>
public sealed class GrubTool : ITool
{
    private static readonly TimeSpan GrubTimeout = TimeSpan.FromMinutes(5);
    private readonly IProcessRunner _runner;
    private readonly ChrootRunner _chroot;

    public GrubTool(IProcessRunner runner, ChrootRunner chroot)
    {
        _runner = runner;
        _chroot = chroot;
    }

    public string Name => "grub";

    /// <summary>
    /// Installs the UEFI bootloader. Uses <c>--efi-directory</c> / <c>--boot-directory</c>
    /// to write directly into the target partition (the EFI partition must be mounted to <paramref name="efiMountPath"/> first),
    /// without relying on the in-chroot environment; the Secure Boot chain takes effect via shim-signed.
    /// </summary>
    public async Task InstallUefiAsync(string targetRoot, string efiMountPath, CancellationToken ct)
    {
        await _runner.RunAsync(
            "grub-install",
            [
                "--target=x86_64-efi",
                "--efi-directory=" + efiMountPath,
                "--boot-directory=" + targetRoot + "/boot",
                "--bootloader-id=FortOS",
                "--no-nvram", // don't write NVRAM in the live environment, avoiding host pollution; the target system is booted via the shim chain on first start
                "--modules=part_gpt",
            ],
            ct,
            timeout: GrubTimeout).ConfigureAwait(false);
    }

    /// <summary>Installs the Legacy BIOS bootloader to the disk MBR.</summary>
    public async Task InstallBiosAsync(string targetRoot, string disk, CancellationToken ct)
    {
        await _runner.RunAsync(
            "grub-install",
            ["--target=i386-pc", "--boot-directory=" + targetRoot + "/boot", disk],
            ct,
            timeout: GrubTimeout).ConfigureAwait(false);
    }

    /// <summary>Generates grub.cfg inside the chroot (requires the /etc/grub.d script environment).</summary>
    public async Task MakeConfigAsync(string targetRoot, CancellationToken ct)
    {
        await _chroot.RunScriptAsync(
            targetRoot,
            "grub-mkconfig -o /boot/grub/grub.cfg",
            ct,
            timeout: GrubTimeout).ConfigureAwait(false);
    }
}
