using FortOS.Installer.Core.Models;
using FortOS.Installer.Core.Session;
using FortOS.Installer.Core.Tools;

namespace FortOS.Installer.Core.Steps;

/// <summary>
/// Format step: format the system disk partitions (EFI/root/swap), mount root and
/// EFI to the target, format the data disk (single disk), and collect UUIDs.
/// </summary>
public sealed class FormatStep : IInstallStep
{
    private readonly MkfsTool _mkfs;
    private readonly BlkidTool _blkid;
    private readonly IProcessRunner _runner;

    public FormatStep(MkfsTool mkfs, BlkidTool blkid, IProcessRunner runner)
    {
        _mkfs = mkfs;
        _blkid = blkid;
        _runner = runner;
    }

    public string Name => "Format";

    public InstallerPhase Phase => InstallerPhase.Formatting;

    public async Task ExecuteAsync(InstallContext context, CancellationToken ct)
    {
        var target = context.TargetMount;
        var rootFs = ToPartitionFs(context.Config.RootFs);

        foreach (var spec in context.SystemPartitions)
        {
            var device = context.SystemPartitionDevices[spec.Number];
            // The filesystem is decided by the GPT type code; the template spec.Fs only serves as a fallback for unknown type codes.
            var fs = spec.TypeCode switch
            {
                GptTypeCode.EfiSystem => PartitionFs.Vfat,
                GptTypeCode.LinuxSwap => PartitionFs.Swap,
                GptTypeCode.LinuxX8664Root => rootFs, // root filesystem is decided by config (template Fs does not apply)
                _ => spec.Fs,
            };
            if (fs == PartitionFs.None)
            {
                continue; // BIOS boot, etc., does not need formatting
            }

            await _mkfs.FormatAsync(device, fs, LabelFor(fs), ct).ConfigureAwait(false);
        }

        // Collect the system disk UUIDs and mount to the target.
        var rootDevice = context.SystemPartitionDevices[FindRootNumber(context)];
        await CollectUuidAsync(context, "root", rootDevice, ct).ConfigureAwait(false);

        Directory.CreateDirectory(target);
        await _runner.RunAsync("mount", [rootDevice, target], ct).ConfigureAwait(false);
        // Note: boot/efi must be created after the root mount — a directory
        // created before mounting is hidden by the new filesystem's root (a real
        // ordering bug hit in production).
        Directory.CreateDirectory($"{target}/boot/efi");

        // Locate the EFI partition by type code (do not use a fixed partition number; stay decoupled from the template).
        var efiSpec = context.SystemPartitions.FirstOrDefault(s => s.TypeCode == GptTypeCode.EfiSystem);
        if (efiSpec is not null && context.SystemPartitionDevices.TryGetValue(efiSpec.Number, out var efiDevice))
        {
            await CollectUuidAsync(context, "efi", efiDevice, ct).ConfigureAwait(false);
            await _runner.RunAsync("mount", [efiDevice, $"{target}/boot/efi"], ct).ConfigureAwait(false);
        }

        var swapSpec = context.SystemPartitions.FirstOrDefault(s => s.TypeCode == GptTypeCode.LinuxSwap);
        if (swapSpec is not null)
        {
            // Swap is intentionally NOT mounted during install: the live environment has its own
            // memory profile, and mounting the target's swap here would be pointless work. The UUID
            // is still collected so ChrootStep can write a correct fstab entry; swapon -a activates
            // it on first boot of the installed system.
            await CollectUuidAsync(context, "swap", context.SystemPartitionDevices[swapSpec.Number], ct).ConfigureAwait(false);
        }

        // Data disk: single/RAID formats directly; LUKS records the container UUID first (crypttab) then formats the mapper.
        if (context.Config.Data.Mode != DataDiskMode.None && context.DataDevice is not null)
        {
            var dataFs = ToPartitionFs(context.Config.Data.FileSystem);

            if (context.Config.Data.Mode == DataDiskMode.Luks && context.DataSourceDevice is not null)
            {
                await CollectUuidAsync(context, "data-luks", context.DataSourceDevice, ct).ConfigureAwait(false);
            }

            await _mkfs.FormatAsync(context.DataDevice, dataFs, context.Config.Data.Label, ct).ConfigureAwait(false);
            await CollectUuidAsync(context, "data", context.DataDevice, ct).ConfigureAwait(false);
            context.Summary.DataFs = context.Config.Data.FileSystem.ToString().ToLowerInvariant();
        }

        context.Summary.SystemRootFs = context.Config.RootFs.ToString().ToLowerInvariant();
        context.Summary.BootMode = context.BootMode?.ToLowerInvariant();
        // Backfill UUIDs into the summary (install-summary.json); TryGetValue is used to avoid extension-method dependencies.
        context.Summary.RootUuid = context.Uuids.TryGetValue("root", out var rootUuid) ? rootUuid : null;
        context.Summary.EfiUuid = context.Uuids.TryGetValue("efi", out var efiUuid) ? efiUuid : null;
        context.Summary.DataUuid = context.Uuids.TryGetValue("data", out var dataUuid) ? dataUuid : null;
    }

    private static int FindRootNumber(InstallContext context)
    {
        var root = context.SystemPartitions.FirstOrDefault(s => s.TypeCode == GptTypeCode.LinuxX8664Root);
        return root?.Number ?? throw new Exceptions.StepException("Format", "System partition template has no root (8304) partition.");
    }

    private static string? LabelFor(PartitionFs fs) => fs switch
    {
        PartitionFs.Vfat => "FORTOS_EFI",
        PartitionFs.Swap => "swap",
        _ => null,
    };

    private static PartitionFs ToPartitionFs(RootFileSystem fs) => fs switch
    {
        RootFileSystem.Ext4 => PartitionFs.Ext4,
        RootFileSystem.Btrfs => PartitionFs.Btrfs,
        _ => PartitionFs.Ext4,
    };

    private static PartitionFs ToPartitionFs(DataFileSystem fs) => fs switch
    {
        DataFileSystem.Ext4 => PartitionFs.Ext4,
        DataFileSystem.Xfs => PartitionFs.Xfs,
        DataFileSystem.Btrfs => PartitionFs.Btrfs,
        _ => PartitionFs.Ext4,
    };

    private async Task CollectUuidAsync(InstallContext context, string role, string device, CancellationToken ct)
    {
        var uuid = await _blkid.GetUuidAsync(device, ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(uuid))
        {
            context.Uuids[role] = uuid;
        }
    }
}
