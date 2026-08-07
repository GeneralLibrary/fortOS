using FortOS.Installer.Core.Exceptions;
using FortOS.Installer.Core.Logging;
using FortOS.Installer.Core.Models;
using FortOS.Installer.Core.Steps;
using FortOS.Installer.Core.Tools;

namespace FortOS.Installer.Core.Session;

/// <summary>
/// Installation session: state-machine orchestration (design doc 5.1). CollectInfo → sequential steps → Done/Failed.
/// Retryable after failure; logs are written twice (in-memory ring buffer + to disk).
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

    /// <summary>Current phase.</summary>
    public InstallerPhase Phase { get; private set; } = InstallerPhase.Idle;

    /// <summary>Result of the most recent run.</summary>
    public InstallResult? LastResult { get; private set; }

    /// <summary>All logs so far.</summary>
    public IReadOnlyList<InstallLogEntry> Logs => _log.Snapshot();

    /// <summary>Phase transition event.</summary>
    public event Action<InstallerPhase>? PhaseChanged;

    /// <summary>New log entry.</summary>
    public event Action<InstallLogEntry>? LogEntryAdded;

    /// <summary>Step progress.</summary>
    public event Action<InstallStepProgress>? StepProgress;

    /// <summary>
    /// Builds the default session for production (live environment, root privileges).
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
    /// Runs the full installation. Any step failure → Failed phase and a failed result is returned (not thrown).
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
                    // Pass CancellationToken.None: ct is already cancelled, so umount would be cancelled immediately and cleanup could not complete.
                    await TryCleanupAsync(context, CancellationToken.None).ConfigureAwait(false);
                    LastResult = new InstallResult { Success = false, FailedStep = step.Name, ErrorMessage = "Cancelled" };
                    return LastResult;
                }
                catch (Exception ex)
                {
                    SetPhase(InstallerPhase.Failed);
                    _log.Error($"Step {step.Name} failed: {ex.Message}");
                    // Cleanup likewise uses CancellationToken.None: if the user requests cancellation right at the moment of failure,
                    // passing ct would cancel umount immediately and leave residual mounts silently leaked.
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

    /// <summary>Unmounts residual mounts on failure/cancellation to keep the install re-runnable; a failed cleanup does not mask the original error.</summary>
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
                // Every member disk must pass the wipe guardrails: exists, usable, not the system disk, and distinct from other members.
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
    /// Data safety guardrail: refuses read-only media (e.g. live CD-ROM) or disks with active mounts as targets,
    /// preventing a misconfiguration from wiping an in-use system.
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

    /// <summary>Determines whether any partition on the disk is currently mounted (mounts is the content of /proc/mounts).</summary>
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
            // Partition names: /dev/sda2 (direct numeric suffix) or /dev/nvme0n1p1 (p prefix).
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
            // The live environment always has /proc/mounts; if reading it fails the guard silently fails, so warn instead of pretending all is well.
            _log.Warn($"Could not read /proc/mounts — in-use disk guard skipped: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// Config validation (shared by CLI and engine; the CLI calls it before printing the confirmation, the engine at the start of RunAsync).
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
        // The username must be a POSIX-safe subset to prevent injection into shell scripts run inside chroot.
        if (string.IsNullOrWhiteSpace(config.Account.Username) ||
            !UsernameRegex().IsMatch(config.Account.Username))
        {
            throw new ConfigException("account.username must match ^[a-z_][a-z0-9_-]{0,31}$.");
        }
        // The timezone may only contain letters/digits/underscore/sign/slash to prevent symlink targets escaping zoneinfo.
        if (string.IsNullOrWhiteSpace(config.Account.Timezone) ||
            !TimezoneRegex().IsMatch(config.Account.Timezone))
        {
            throw new ConfigException("account.timezone contains invalid characters.");
        }
        // The password is passed via chpasswd stdin: colons/newlines could truncate or inject entries, so they must be rejected.
        if (config.Account.Password.Contains(':') || config.Account.Password.Contains('\n') || config.Account.Password.Contains('\r'))
        {
            throw new ConfigException("account.password must not contain ':' or newlines.");
        }
        // The data disk label is passed to mkfs -L/-n, so restrict it to safe characters and length.
        if (!LabelRegex().IsMatch(config.Data.Label))
        {
            throw new ConfigException("data.label may only contain [A-Za-z0-9_-] (max 16 chars).");
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex("^[a-z_][a-z0-9_-]{0,31}$")]
    private static partial System.Text.RegularExpressions.Regex UsernameRegex();

    [System.Text.RegularExpressions.GeneratedRegex("^[A-Za-z0-9_+\\-/]+$")]
    private static partial System.Text.RegularExpressions.Regex TimezoneRegex();

    /// <summary>Device names (RaidDeviceName / LuksMapperName) may only contain safe characters to prevent path injection.</summary>
    [System.Text.RegularExpressions.GeneratedRegex("^[A-Za-z0-9_.-]+$")]
    private static partial System.Text.RegularExpressions.Regex SafeNameRegex();

    /// <summary>Safe characters for the data disk label (passed to mkfs -L/-n).</summary>
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
