using System.Text.Json;
using FortOS.Installer.Core.Models;
using FortOS.Installer.Core.Session;
using FortOS.Installer.Core.Tools;

namespace FortOS.Installer.Core.Steps;

/// <summary>
/// Finalize step: write the install summary and log to the target system, unmount
/// mounts, and close LUKS/RAID devices. Success and failure paths share
/// <see cref="UnmountAsync"/>, preserving the "re-runnable" semantics (design doc 5.1).
/// </summary>
public sealed class FinalizeStep : IInstallStep
{
    private readonly ChrootRunner _chroot;
    private readonly IProcessRunner _runner;
    private readonly CryptsetupTool _cryptsetup;
    private readonly MdadmTool _mdadm;
    private readonly Func<IReadOnlyList<InstallLogEntry>> _logProvider;

    public FinalizeStep(
        ChrootRunner chroot,
        IProcessRunner runner,
        CryptsetupTool cryptsetup,
        MdadmTool mdadm,
        Func<IReadOnlyList<InstallLogEntry>> logProvider)
    {
        _chroot = chroot;
        _runner = runner;
        _cryptsetup = cryptsetup;
        _mdadm = mdadm;
        _logProvider = logProvider;
    }

    public string Name => "Finalize";

    public InstallerPhase Phase => InstallerPhase.Finalize;

    public async Task ExecuteAsync(InstallContext context, CancellationToken ct)
    {
        var target = context.TargetMount;

        context.Summary.FinishedAt = DateTimeOffset.UtcNow;
        context.Summary.Success = true;

        // The target partition is still mounted: write the summary and log to the target rootfs first.
        TargetFileWriter.Write(target, "etc/fortos/install-summary.json", JsonSerializer.Serialize(context.Summary, InstallerJsonContext.Default.InstallSummary));
        var logText = string.Join('\n', _logProvider().Select(l => $"{l.Timestamp:yyyy-MM-dd HH:mm:ss} [{l.Level}] {l.Message}"));
        TargetFileWriter.Write(target, "var/log/fortos-install.log", logText + "\n");

        await UnmountAsync(_chroot, _runner, _cryptsetup, _mdadm, context, ct).ConfigureAwait(false);
        // A sync failure should not mark the completed install as failed (data is already on disk; it heals on reboot).
        await _runner.RunAsync("sync", [], ct, throwOnNonZeroExit: false).ConfigureAwait(false);
    }

    /// <summary>
    /// Unmounts/closes devices held during the install, shared by success and
    /// failure paths: unmount bind mounts and the target partition → close LUKS
    /// mappings / stop RAID arrays. Fully fault-tolerant throughout (failures of
    /// umount/cryptsetup close/mdadm --stop only warn), preserving the
    /// "re-runnable" semantics (design doc 5.1).
    /// </summary>
    internal static async Task UnmountAsync(
        ChrootRunner chroot,
        IProcessRunner runner,
        CryptsetupTool cryptsetup,
        MdadmTool mdadm,
        InstallContext context,
        CancellationToken ct)
    {
        var target = context.TargetMount;
        await chroot.UnmountAllAsync(target, ct).ConfigureAwait(false);
        await runner.RunAsync("umount", [$"{target}/boot/efi"], ct, throwOnNonZeroExit: false).ConfigureAwait(false);
        await runner.RunAsync("umount", [target], ct, throwOnNonZeroExit: false).ConfigureAwait(false);

        // Data disk devices: stop RAID arrays / close LUKS mappings, otherwise the devices already exist on retry.
        if (context.Config.Data.Mode == DataDiskMode.Raid && context.DataDevice is not null)
        {
            await mdadm.StopAsync(context.DataDevice, ct).ConfigureAwait(false);
        }
        else if (context.Config.Data.Mode == DataDiskMode.Luks)
        {
            await cryptsetup.LuksCloseAsync(context.Config.Data.LuksMapperName, ct).ConfigureAwait(false);
        }
    }
}
