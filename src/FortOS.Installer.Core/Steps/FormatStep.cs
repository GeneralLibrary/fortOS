using FortOS.Installer.Core.Models;
using FortOS.Installer.Core.Session;
using FortOS.Installer.Core.Tools;

namespace FortOS.Installer.Core.Steps;

/// <summary>
/// 格式化步骤:格式化系统盘各分区(EFI/根/swap)、挂载 root 与 EFI 到目标、
/// 格式化数据盘(单盘)并收集 UUID。
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
            // 按 GPT 类型码决定文件系统;模板 spec.Fs 仅作未知类型码的兜底。
            var fs = spec.TypeCode switch
            {
                GptTypeCode.EfiSystem => PartitionFs.Vfat,
                GptTypeCode.LinuxSwap => PartitionFs.Swap,
                GptTypeCode.LinuxX8664Root => rootFs, // 根分区文件系统由配置决定(模板 Fs 不适用)
                _ => spec.Fs,
            };
            if (fs == PartitionFs.None)
            {
                continue; // BIOS boot 等无需格式化
            }

            await _mkfs.FormatAsync(device, fs, LabelFor(fs), ct).ConfigureAwait(false);
        }

        // 收集系统盘 UUID 并挂载到目标。
        var rootDevice = context.SystemPartitionDevices[FindRootNumber(context)];
        await CollectUuidAsync(context, "root", rootDevice, ct).ConfigureAwait(false);

        Directory.CreateDirectory(target);
        await _runner.RunAsync("mount", [rootDevice, target], ct).ConfigureAwait(false);
        // 注意:boot/efi 必须在 root 挂载之后创建——挂载前创建的目录会被
        // 新文件系统的根目录覆盖(真实环境踩到的顺序 bug)。
        Directory.CreateDirectory($"{target}/boot/efi");

        // EFI 分区按类型码定位(勿用固定分区号,与模板解耦)。
        var efiSpec = context.SystemPartitions.FirstOrDefault(s => s.TypeCode == GptTypeCode.EfiSystem);
        if (efiSpec is not null && context.SystemPartitionDevices.TryGetValue(efiSpec.Number, out var efiDevice))
        {
            await CollectUuidAsync(context, "efi", efiDevice, ct).ConfigureAwait(false);
            await _runner.RunAsync("mount", [efiDevice, $"{target}/boot/efi"], ct).ConfigureAwait(false);
        }

        var swapSpec = context.SystemPartitions.FirstOrDefault(s => s.TypeCode == GptTypeCode.LinuxSwap);
        if (swapSpec is not null)
        {
            await CollectUuidAsync(context, "swap", context.SystemPartitionDevices[swapSpec.Number], ct).ConfigureAwait(false);
        }

        // 数据盘:单盘/RAID 直接格式化;LUKS 先记录容器 UUID(crypttab)再格式化 mapper。
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
        // 回填 UUID 到摘要(install-summary.json);用 TryGetValue 避免扩展方法依赖。
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
