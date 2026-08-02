using FortOS.Installer.Core.Exceptions;
using FortOS.Installer.Core.Logging;
using FortOS.Installer.Core.Models;
using FortOS.Installer.Core.Steps;
using FortOS.Installer.Core.Tools;

namespace FortOS.Installer.Core.Session;

/// <summary>
/// 安装会话:状态机编排(设计稿 5.1)。CollectInfo → 各步骤顺序执行 → Done/Failed。
/// 失败后可重试;日志双写(内存环形缓冲 + 落盘)。
/// </summary>
public sealed partial class InstallerSession
{
    private readonly IReadOnlyList<IInstallStep> _steps;
    private readonly LsblkTool _lsblk;
    private readonly RingLog _log;
    private readonly Func<InstallContext, CancellationToken, Task>? _cleanupTarget;

    public InstallerSession(IEnumerable<IInstallStep> steps, LsblkTool lsblk, RingLog? log = null, Func<InstallContext, CancellationToken, Task>? cleanupTarget = null)
    {
        _steps = [.. steps];
        _lsblk = lsblk;
        _log = log ?? new RingLog();
        _cleanupTarget = cleanupTarget;
        _log.EntryAdded += entry => LogEntryAdded?.Invoke(entry);
    }

    /// <summary>当前阶段。</summary>
    public InstallerPhase Phase { get; private set; } = InstallerPhase.Idle;

    /// <summary>最近一次运行结果。</summary>
    public InstallResult? LastResult { get; private set; }

    /// <summary>当前全部日志。</summary>
    public IReadOnlyList<InstallLogEntry> Logs => _log.Snapshot();

    /// <summary>阶段切换事件。</summary>
    public event Action<InstallerPhase>? PhaseChanged;

    /// <summary>新日志条目。</summary>
    public event Action<InstallLogEntry>? LogEntryAdded;

    /// <summary>步骤进度。</summary>
    public event Action<InstallStepProgress>? StepProgress;

    /// <summary>
    /// 组装生产环境默认会话(live 环境,root 权限)。
    /// </summary>
    public static InstallerSession CreateDefault(IProcessRunner? runner = null)
    {
        var processRunner = runner ?? new ProcessRunner();
        var log = new RingLog();
        var lsblk = new LsblkTool(processRunner);
        var chroot = new ChrootRunner(processRunner);
        var grub = new GrubTool(processRunner, chroot);
        var cryptsetup = new CryptsetupTool(processRunner);
        var mdadm = new MdadmTool(processRunner);

        IInstallStep[] steps =
        [
            new PartitionStep(new SgdiskTool(processRunner), mdadm, cryptsetup),
            new FormatStep(new MkfsTool(processRunner), new BlkidTool(processRunner), processRunner),
            new CopyStep(new RsyncTool(processRunner)),
            new ChrootStep(chroot),
            new BootloaderStep(grub, chroot),
            new FinalizeStep(chroot, processRunner, cryptsetup, mdadm, () => log.Snapshot()),
        ];

        return new InstallerSession(
            steps,
            lsblk,
            log,
            cleanupTarget: (ctx, c) => FinalizeStep.UnmountAsync(chroot, processRunner, cryptsetup, mdadm, ctx, c));
    }

    /// <summary>
    /// 执行完整安装。任何步骤失败 → Failed 阶段并返回失败结果(不抛出)。
    /// </summary>
    public async Task<InstallResult> RunAsync(InstallConfig config, CancellationToken ct)
    {
        ValidateConfig(config);
        SetPhase(InstallerPhase.CollectInfo);
        _log.Info($"Starting FortOS installation: system={config.SystemDisk}, rootfs={config.RootFs}, boot={config.Bootloader}");

        var context = new InstallContext
        {
            Config = config,
            SourcePath = string.IsNullOrWhiteSpace(config.SourcePath) ? "/" : config.SourcePath,
        };

        try
        {
            await CollectInfoAsync(context, ct).ConfigureAwait(false);

            foreach (var step in _steps)
            {
                SetPhase(step.Phase);
                StepProgress?.Invoke(new InstallStepProgress(step.Name, 0, $"Starting {step.Name}"));
                _log.Info($"--- Step: {step.Name} ---");
                try
                {
                    await step.ExecuteAsync(context, ct).ConfigureAwait(false);
                    StepProgress?.Invoke(new InstallStepProgress(step.Name, 100, $"Finished {step.Name}"));
                    _log.Info($"Step {step.Name} completed.");
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    SetPhase(InstallerPhase.Failed);
                    _log.Warn("Installation cancelled by user.");
                    // 传 CancellationToken.None:ct 已取消,umount 会被立即取消而无法完成清理。
                    await TryCleanupAsync(context, CancellationToken.None).ConfigureAwait(false);
                    LastResult = new InstallResult { Success = false, FailedStep = step.Name, ErrorMessage = "Cancelled" };
                    return LastResult;
                }
                catch (Exception ex)
                {
                    SetPhase(InstallerPhase.Failed);
                    _log.Error($"Step {step.Name} failed: {ex.Message}");
                    // 清理同样用 CancellationToken.None:若用户恰在失败瞬间请求取消,
                    // 传 ct 会让 umount 立即被取消,残留挂载静默泄漏。
                    await TryCleanupAsync(context, CancellationToken.None).ConfigureAwait(false);
                    LastResult = new InstallResult { Success = false, FailedStep = step.Name, ErrorMessage = ex.Message };
                    return LastResult;
                }
            }

            SetPhase(InstallerPhase.Done);
            _log.Info("FortOS installation completed successfully.");
            LastResult = new InstallResult { Success = true, Summary = context.Summary };
            return LastResult;
        }
        catch (Exception ex)
        {
            SetPhase(InstallerPhase.Failed);
            _log.Error($"Installation failed: {ex.Message}");
            LastResult = new InstallResult { Success = false, FailedStep = null, ErrorMessage = ex.Message };
            return LastResult;
        }
    }

    /// <summary>失败/取消时卸载残留挂载,保证「可重跑」;清理失败不覆盖原始错误。</summary>
    private async Task TryCleanupAsync(InstallContext context, CancellationToken ct)
    {
        if (_cleanupTarget is null)
        {
            return;
        }
        try
        {
            await _cleanupTarget(context, ct).ConfigureAwait(false);
            _log.Info("Cleaned up mounts after failure.");
        }
        catch (Exception ex)
        {
            _log.Warn($"Cleanup failed (non-fatal): {ex.Message}");
        }
    }

    private async Task CollectInfoAsync(InstallContext context, CancellationToken ct)
    {
        _log.Info("Collecting environment information...");
        var disks = await _lsblk.ListDisksAsync(ct).ConfigureAwait(false);

        EnsureDiskExists(disks, context.Config.SystemDisk, "system.disk");
        EnsureDiskUsable(disks, context.Config.SystemDisk, "system.disk");
        switch (context.Config.Data.Mode)
        {
            case DataDiskMode.Single:
                EnsureDataDisk(disks, context.Config.Data.Disk!, context.Config.SystemDisk);
                break;
            case DataDiskMode.Raid:
                // 每个成员盘都要过清盘护栏:存在、可用、非系统盘、成员两两不同。
                foreach (var member in context.Config.Data.RaidDisks)
                {
                    EnsureDataDisk(disks, member, context.Config.SystemDisk);
                }
                if (context.Config.Data.RaidDisks.Distinct().Count() != context.Config.Data.RaidDisks.Count)
                {
                    throw new ConfigException("data.raidDisks must not contain duplicate disks.");
                }
                break;
            case DataDiskMode.Luks:
                EnsureDataDisk(disks, context.Config.Data.Disk!, context.Config.SystemDisk);
                break;
        }

        context.Summary.StartedAt = DateTimeOffset.UtcNow;
        _log.Info($"Detected {disks.Count} disk(s); target system disk: {context.Config.SystemDisk}.");
    }

    private void EnsureDataDisk(IReadOnlyList<DiskInfo> disks, string dataDisk, string systemDisk)
    {
        EnsureDiskExists(disks, dataDisk, "data.disk");
        EnsureDiskUsable(disks, dataDisk, "data.disk");
        if (dataDisk == systemDisk)
        {
            throw new ConfigException("data.disk must be different from system.disk.");
        }
    }

    private static void EnsureDiskExists(IReadOnlyList<DiskInfo> disks, string diskPath, string field)
    {
        if (string.IsNullOrWhiteSpace(diskPath))
        {
            throw new ConfigException($"{field} must be specified.");
        }
        if (disks.All(d => d.Path != diskPath))
        {
            throw new ConfigException($"{field} {diskPath} was not found by lsblk.");
        }
    }

    /// <summary>
    /// 数据安全护栏:拒绝把只读介质(如 live CD-ROM)或当前挂载中的盘作为目标,
    /// 防止误配导致正在使用的系统被清盘。
    /// </summary>
    private void EnsureDiskUsable(IReadOnlyList<DiskInfo> disks, string diskPath, string field)
    {
        var disk = disks.First(d => d.Path == diskPath);
        if (disk.IsReadOnly)
        {
            throw new ConfigException($"{field} {diskPath} is read-only (live media?) — refusing to wipe it.");
        }
        if (IsDiskInUse(diskPath, ReadMounts()))
        {
            throw new ConfigException($"{field} {diskPath} has mounted partitions — refusing to wipe an in-use disk.");
        }
    }

    /// <summary>判断磁盘上是否有分区正被挂载(mounts 为 /proc/mounts 内容)。</summary>
    internal static bool IsDiskInUse(string diskPath, string mountsText)
    {
        foreach (var line in mountsText.Split('\n'))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }
            var source = parts[0];
            if (source == diskPath)
            {
                return true;
            }
            // 分区名:/dev/sda2(直接数字后缀)或 /dev/nvme0n1p1(p 前缀)。
            if (source.StartsWith(diskPath, StringComparison.Ordinal) && source.Length > diskPath.Length)
            {
                var rest = source[diskPath.Length..];
                if (char.IsDigit(rest[0]) || (rest[0] == 'p' && rest.Length > 1 && char.IsDigit(rest[1])))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private string ReadMounts()
    {
        try
        {
            return File.ReadAllText("/proc/mounts");
        }
        catch (Exception ex)
        {
            // live 环境必有 /proc/mounts;读取失败时护栏静默失效,必须告警而非假装无事。
            _log.Warn($"Could not read /proc/mounts — in-use disk guard skipped: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// 配置校验(CLI 与引擎共用;CLI 在打印确认前调用,引擎在 RunAsync 开头调用)。
    /// </summary>
    public static void ValidateConfig(InstallConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.SystemDisk))
        {
            throw new ConfigException("system.disk must be specified.");
        }
        if (config.Data.Mode == DataDiskMode.Single && string.IsNullOrWhiteSpace(config.Data.Disk))
        {
            throw new ConfigException("data.disk must be specified when data.mode is 'single'.");
        }
        if (config.Data.Mode == DataDiskMode.Raid)
        {
            if (config.Data.RaidDisks.Count < 2)
            {
                throw new ConfigException("data.raidDisks (>= 2 disks) must be specified when data.mode is 'raid'.");
            }
            var minimum = config.Data.RaidLevel switch
            {
                1 => 2,
                5 => 3,
                10 => 4,
                _ => throw new ConfigException("data.raidLevel must be 1, 5 or 10."),
            };
            if (config.Data.RaidDisks.Count < minimum)
            {
                throw new ConfigException($"RAID{config.Data.RaidLevel} requires at least {minimum} member disks (got {config.Data.RaidDisks.Count}).");
            }
            if (!SafeNameRegex().IsMatch(config.Data.RaidDeviceName))
            {
                throw new ConfigException("data.raidDeviceName may only contain [A-Za-z0-9_.-].");
            }
        }
        if (config.Data.Mode == DataDiskMode.Luks)
        {
            if (string.IsNullOrWhiteSpace(config.Data.Disk))
            {
                throw new ConfigException("data.disk must be specified when data.mode is 'luks'.");
            }
            if (string.IsNullOrEmpty(config.Data.LuksPassphrase))
            {
                throw new ConfigException("data.luksPassphrase must be specified when data.mode is 'luks'.");
            }
            if (!SafeNameRegex().IsMatch(config.Data.LuksMapperName))
            {
                throw new ConfigException("data.luksMapperName may only contain [A-Za-z0-9_.-].");
            }
        }
        if (config.Network.Mode == NetworkMode.Static && string.IsNullOrWhiteSpace(config.Network.Address))
        {
            throw new ConfigException("network.address must be specified when network.mode is 'static'.");
        }
        if (config.Network.Mode == NetworkMode.Static && !config.Network.Address!.Contains('/'))
        {
            throw new ConfigException("network.address must be a CIDR address, e.g. 192.168.1.10/24.");
        }
        // 用户名必须是 POSIX 安全子集,防止注入 chroot 内的 shell 脚本。
        if (string.IsNullOrWhiteSpace(config.Account.Username) ||
            !UsernameRegex().IsMatch(config.Account.Username))
        {
            throw new ConfigException("account.username must match ^[a-z_][a-z0-9_-]{0,31}$.");
        }
        // 时区只允许字母/数字/下划线/正负号/斜杠,防止符号链接目标逃逸 zoneinfo。
        if (string.IsNullOrWhiteSpace(config.Account.Timezone) ||
            !TimezoneRegex().IsMatch(config.Account.Timezone))
        {
            throw new ConfigException("account.timezone contains invalid characters.");
        }
        // 密码经 chpasswd stdin 传递:冒号/换行会截断或注入条目,必须拒绝。
        if (config.Account.Password.Contains(':') || config.Account.Password.Contains('\n') || config.Account.Password.Contains('\r'))
        {
            throw new ConfigException("account.password must not contain ':' or newlines.");
        }
        // 数据盘卷标会传给 mkfs -L/-n,限定安全字符与长度。
        if (!LabelRegex().IsMatch(config.Data.Label))
        {
            throw new ConfigException("data.label may only contain [A-Za-z0-9_-] (max 16 chars).");
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex("^[a-z_][a-z0-9_-]{0,31}$")]
    private static partial System.Text.RegularExpressions.Regex UsernameRegex();

    [System.Text.RegularExpressions.GeneratedRegex("^[A-Za-z0-9_+\\-/]+$")]
    private static partial System.Text.RegularExpressions.Regex TimezoneRegex();

    /// <summary>设备名(RaidDeviceName / LuksMapperName)只允许安全字符,防止路径注入。</summary>
    [System.Text.RegularExpressions.GeneratedRegex("^[A-Za-z0-9_.-]+$")]
    private static partial System.Text.RegularExpressions.Regex SafeNameRegex();

    /// <summary>数据盘卷标安全字符(传给 mkfs -L/-n)。</summary>
    [System.Text.RegularExpressions.GeneratedRegex("^[A-Za-z0-9_-]{1,16}$")]
    private static partial System.Text.RegularExpressions.Regex LabelRegex();

    private void SetPhase(InstallerPhase phase)
    {
        if (Phase == phase)
        {
            return;
        }
        Phase = phase;
        PhaseChanged?.Invoke(phase);
    }
}
