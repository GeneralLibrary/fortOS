using FortOS.Installer.Core.Models;
using FortOS.Installer.Core.Session;
using FortOS.Installer.Core.Tools;

namespace FortOS.Installer.Core.Steps;

/// <summary>
/// 分区步骤:清盘 → 按模板创建 GPT 分区 → 校验 → 等待设备节点 → 确定引导方式;
/// 数据盘(单盘)同时创建分区。
/// </summary>
public sealed class PartitionStep : IInstallStep
{
    private readonly SgdiskTool _sgdisk;
    private readonly MdadmTool _mdadm;
    private readonly CryptsetupTool _cryptsetup;

    public PartitionStep(SgdiskTool sgdisk, MdadmTool mdadm, CryptsetupTool cryptsetup)
    {
        _sgdisk = sgdisk;
        _mdadm = mdadm;
        _cryptsetup = cryptsetup;
    }

    public string Name => "Partition";

    public InstallerPhase Phase => InstallerPhase.Partitioning;

    public async Task ExecuteAsync(InstallContext context, CancellationToken ct)
    {
        var disk = context.Config.SystemDisk;
        await _sgdisk.ZapAsync(disk, ct).ConfigureAwait(false);

        var swapMiB = ResolveSwapMiB(context.Config);
        var specs = PartitionTemplates.SystemDefault(swapMiB);
        context.SystemPartitions.AddRange(specs);
        await _sgdisk.CreatePartitionsAsync(disk, specs, ct).ConfigureAwait(false);
        await _sgdisk.VerifyAsync(disk, ct).ConfigureAwait(false);

        // 等待 udev 生成分区设备节点。
        foreach (var spec in specs)
        {
            var device = PartitionDevice(disk, spec.Number);
            await WaitForDeviceAsync(device, ct).ConfigureAwait(false);
            context.SystemPartitionDevices[spec.Number] = device;
        }

        context.BootMode = ResolveBootMode(context.Config.Bootloader);
        context.Summary.SystemDisk = disk;

        if (context.Config.Data.Mode != DataDiskMode.None)
        {
            await CreateDataDiskAsync(context, ct).ConfigureAwait(false);
        }
    }

    private async Task CreateDataDiskAsync(InstallContext context, CancellationToken ct)
    {
        switch (context.Config.Data.Mode)
        {
            case DataDiskMode.Single:
                await CreateSingleAsync(context, ct).ConfigureAwait(false);
                break;
            case DataDiskMode.Raid:
                await CreateRaidAsync(context, ct).ConfigureAwait(false);
                break;
            case DataDiskMode.Luks:
                await CreateLuksAsync(context, ct).ConfigureAwait(false);
                break;
        }
    }

    private async Task CreateSingleAsync(InstallContext context, CancellationToken ct)
    {
        var disk = context.Config.Data.Disk
            ?? throw new Exceptions.ConfigException("DataDiskMode.Single requires data.disk in install.yaml.");

        await _sgdisk.ZapAsync(disk, ct).ConfigureAwait(false);
        // 单盘单分区:整盘一个主分区(8300 = Linux filesystem)。
        await _sgdisk.CreatePartitionsAsync(disk, [new PartitionSpec { Number = 1, SizeMiB = 0, TypeCode = GptTypeCode.LinuxFilesystem, Label = "FortOS data", Fs = PartitionFs.None }], ct).ConfigureAwait(false);
        await _sgdisk.VerifyAsync(disk, ct).ConfigureAwait(false);
        var dataDevice = PartitionDevice(disk, 1);
        await WaitForDeviceAsync(dataDevice, ct).ConfigureAwait(false);
        context.DataDevice = dataDevice;
        context.Summary.DataDisk = disk;
    }

    private async Task CreateRaidAsync(InstallContext context, CancellationToken ct)
    {
        var cfg = context.Config.Data;
        if (cfg.RaidDisks.Count < 2)
        {
            throw new Exceptions.ConfigException("RAID requires at least 2 member disks (data.raidDisks).");
        }

        foreach (var disk in cfg.RaidDisks)
        {
            await _sgdisk.ZapAsync(disk, ct).ConfigureAwait(false);
        }

        var device = $"/dev/{cfg.RaidDeviceName}";
        await _mdadm.CreateAsync(device, cfg.RaidLevel, "fortos-data", cfg.RaidDisks, ct).ConfigureAwait(false);
        await WaitForDeviceAsync(device, ct).ConfigureAwait(false);
        context.DataDevice = device;
        context.Summary.DataDisk = string.Join(",", cfg.RaidDisks);
    }

    private async Task CreateLuksAsync(InstallContext context, CancellationToken ct)
    {
        var cfg = context.Config.Data;
        var disk = cfg.Disk
            ?? throw new Exceptions.ConfigException("DataDiskMode.Luks requires data.disk in install.yaml.");
        if (string.IsNullOrEmpty(cfg.LuksPassphrase))
        {
            throw new Exceptions.ConfigException("DataDiskMode.Luks requires data.luksPassphrase.");
        }

        await _sgdisk.ZapAsync(disk, ct).ConfigureAwait(false);
        await _cryptsetup.LuksFormatAsync(disk, cfg.LuksPassphrase, ct).ConfigureAwait(false);

        var mapper = $"/dev/mapper/{cfg.LuksMapperName}";
        await _cryptsetup.LuksOpenAsync(disk, cfg.LuksMapperName, cfg.LuksPassphrase, ct).ConfigureAwait(false);
        await WaitForDeviceAsync(mapper, ct).ConfigureAwait(false);

        context.DataSourceDevice = disk;
        context.DataDevice = mapper;
        context.Summary.DataDisk = disk;
    }

    /// <summary>等待设备节点出现(udev 延迟容忍;loop 等设备需先关联)。</summary>
    private async Task WaitForDeviceAsync(string device, CancellationToken ct)
    {
        // 轮询 40 × 250ms = 10s 超时。
        const int attempts = 40;
        const int delayMs = 250;
        for (var i = 0; i < attempts; i++)
        {
            if (File.Exists(device))
            {
                return;
            }
            await Task.Delay(delayMs, ct).ConfigureAwait(false);
        }
        throw new Exceptions.StepException(Name, $"Partition device {device} did not appear after partitioning.");
    }

    /// <summary>解析 swap 大小:Auto=内存大小,Fixed=配置值,Off=0。</summary>
    internal static long ResolveSwapMiB(InstallConfig config) => config.SwapMode switch
    {
        SwapMode.Off => 0,
        SwapMode.Fixed => Math.Max(0, config.SwapSizeMiB ?? 0),
        SwapMode.Auto => ReadMemTotalMiB(),
        _ => 0,
    };

    private static long ReadMemTotalMiB()
    {
        try
        {
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    return parts.Length >= 2 && long.TryParse(parts[1], out var kB) ? kB / 1024 : 4096;
                }
            }
        }
        catch
        {
            // 读取失败时给保守默认。
        }
        return 4096;
    }

    /// <summary>检测实际引导方式:配置覆盖优先,否则看固件。</summary>
    internal static BootModeKind ResolveBootMode(BootloaderMode mode) => mode switch
    {
        BootloaderMode.Uefi => BootModeKind.Uefi,
        BootloaderMode.Bios => BootModeKind.Bios,
        _ => Directory.Exists("/sys/firmware/efi") ? BootModeKind.Uefi : BootModeKind.Bios,
    };

    /// <summary>拼接分区设备路径:NVMe/MMC/loop 需要 p 分隔符(loop0 → loop0p1)。</summary>
    internal static string PartitionDevice(string disk, int number)
    {
        var sep = (disk.Contains("nvme", StringComparison.OrdinalIgnoreCase)
                   || disk.Contains("mmcblk", StringComparison.OrdinalIgnoreCase)
                   || disk.Contains("loop", StringComparison.OrdinalIgnoreCase))
            ? "p"
            : string.Empty;
        return $"{disk}{sep}{number}";
    }
}
