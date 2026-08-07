using System.Collections.Concurrent;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using FortOS.Core;
using FortOS.Platform.Execution;
using FortOS.Platform.Linux.Monitoring;
using Microsoft.Extensions.Logging;

namespace FortOS.Platform.Linux;

/// <summary>
/// Linux disk manager.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed partial class LinuxDiskManager : IDiskManager
{
    private readonly CommandExecutor _executor;
    private readonly ILogger<LinuxDiskManager> _logger;

    /// <summary>Serializes partition operations per disk path: without locking the probe→mklabel→mkpart window, two concurrent
    /// operations on a fresh disk would let the later mklabel gpt destroy the partition table the former just created.</summary>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DiskLocks = new(StringComparer.Ordinal);

    /// <summary>Initializes the Linux disk manager.</summary>
    /// <param name="logger">Logger.</param>
    public LinuxDiskManager(ILogger<LinuxDiskManager> logger)
    {
        _logger = logger;
        _executor = new CommandExecutor(logger);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DiskInfo>> ListDisksAsync(CancellationToken ct)
    {
        var result = await _executor.ExecuteAsync("lsblk", "--json -b -o NAME,MODEL,SERIAL,SIZE,TRAN,ROTA,MOUNTPOINT,FSUSE%,TYPE", ct).ConfigureAwait(false);
        using var document = JsonDocument.Parse(result.Stdout);
        var disks = new List<DiskInfo>();
        foreach (var block in document.RootElement.GetProperty("blockdevices").EnumerateArray())
        {
            AddDisk(block, disks);
        }

        // lsblk reports no temperature/health; enrich each disk from SMART in parallel.
        return await EnrichWithSmartAsync(disks, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Overlay SMART temperature and health onto disks (parallel, best-effort —
    /// disks without SMART support keep their lsblk defaults).
    /// </summary>
    private async Task<IReadOnlyList<DiskInfo>> EnrichWithSmartAsync(IReadOnlyList<DiskInfo> disks, CancellationToken ct)
    {
        if (disks.Count == 0) return disks;

        var enriched = await Task.WhenAll(disks.Select(async disk =>
        {
            try
            {
                var smart = await GetSmartDataAsync(disk.Path, ct).ConfigureAwait(false);
                var temperature = smart.TemperatureCelsius ?? disk.TemperatureCelsius;
                if (temperature <= 0 && string.Equals(smart.Health, "Unsupported", StringComparison.OrdinalIgnoreCase))
                {
                    return disk;
                }

                return disk with
                {
                    TemperatureCelsius = temperature,
                    SmartStatus = string.IsNullOrWhiteSpace(smart.Health) || string.Equals(smart.Health, "Unknown", StringComparison.OrdinalIgnoreCase)
                        ? disk.SmartStatus
                        : smart.Health,
                };
            }
            catch (Exception ex) when (ex is PlatformException or CommandExecutionException or JsonException or OperationCanceledException)
            {
                return disk;
            }
        })).ConfigureAwait(false);

        return enriched;
    }

    /// <inheritdoc />
    public async Task<DiskInfo?> GetDiskAsync(string path, CancellationToken ct)
    {
        return (await ListDisksAsync(ct).ConfigureAwait(false)).FirstOrDefault(d => string.Equals(d.Path, path, StringComparison.Ordinal));
    }

    /// <inheritdoc />
    public async Task<PartitionResult> CreatePartitionAsync(string diskPath, PartitionSpec spec, CancellationToken ct)
    {
        ValidateDevicePath(diskPath);
        if (spec.SizeBytes.HasValue && !spec.StartBytes.HasValue)
        {
            // parted's mkpart models the partition as absolute start + absolute end, so a bare
            // size cannot be expressed; refusing beats silently ignoring the size and extending
            // the partition to 100% of the disk.
            throw new ArgumentException("SizeBytes requires StartBytes; a size alone cannot locate the partition end.", nameof(spec));
        }

        // Partition operations on the same disk are fully serialized (probe→mklabel→mkpart) to prevent concurrent partition creation from wiping each other's partition table.
        var diskLock = DiskLocks.GetOrAdd(diskPath, static _ => new SemaphoreSlim(1, 1));
        await diskLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await CreatePartitionCoreAsync(diskPath, spec, ct).ConfigureAwait(false);
        }
        finally
        {
            diskLock.Release();
        }
    }

    private async Task<PartitionResult> CreatePartitionCoreAsync(string diskPath, PartitionSpec spec, CancellationToken ct)
    {
        var start = spec.StartBytes.HasValue ? $"{spec.StartBytes.Value}B" : "1MiB";
        var end = spec.SizeBytes.HasValue ? $"{spec.StartBytes!.Value + spec.SizeBytes.Value}B" : "100%";
        var fsType = string.IsNullOrWhiteSpace(spec.FileSystem) ? "ext4" : ValidateFs(spec.FileSystem);

        // Initialize the GPT label only when the disk has no partition table yet. Never run mklabel gpt
        // unconditionally, otherwise every "add partition" would first destroy the disk's existing partition table (data loss).
        await EnsureDiskLabelAsync(diskPath, ct).ConfigureAwait(false);

        var args = $"--script {Quote(diskPath)} mkpart {Quote(spec.Name)} {Quote(fsType)} {Quote(start)} {Quote(end)}";
        await _executor.ExecuteAsync("parted", args, ct).ConfigureAwait(false);

        // parted does not echo the created partition's device path; read it back with lsblk so
        // callers receive a usable path (e.g. /dev/sda1) instead of the disk itself.
        var partitionPath = await ReadNewestPartitionPathAsync(diskPath, ct).ConfigureAwait(false);
        return new PartitionResult { Success = true, PartitionPath = partitionPath, Message = "Partition created." };
    }

    /// <summary>
    /// Ensures the disk has a partition table. An existing valid label is left untouched; a GPT label is
    /// written only when parted explicitly reports "unrecognised disk label" (a fresh disk). Other errors
    /// (device missing, I/O failure, etc.) are thrown directly to avoid destructive operations on an abnormal device.
    /// </summary>
    private async Task EnsureDiskLabelAsync(string diskPath, CancellationToken ct)
    {
        var probe = await _executor.ExecuteAsync("parted", $"--script --machine {Quote(diskPath)} print", ct, throwOnNonZeroExit: false).ConfigureAwait(false);
        if (probe.ExitCode == 0)
        {
            return;
        }

        if (ShouldInitializeDiskLabel(probe))
        {
            await _executor.ExecuteAsync("parted", $"--script {Quote(diskPath)} mklabel gpt", ct).ConfigureAwait(false);
            return;
        }

        var diagnostic = string.Join(' ', new[] { probe.Stdout, probe.Stderr }.Where(static line => !string.IsNullOrWhiteSpace(line)));
        throw new PlatformException($"Cannot inspect disk {diskPath}: {diagnostic}");
    }

    /// <summary>
    /// Determines whether the disk needs a GPT label: returns true only when parted explicitly reports
    /// "unrecognised disk label" (a fresh disk); returns false when probing fails for an unknown reason
    /// (device missing, etc.), and the caller will refuse the destructive operation. Kept as a pure function
    /// so regression tests can pin down the F1 data-protection semantics.
    /// </summary>
    internal static bool ShouldInitializeDiskLabel(CommandResult probe)
        => probe.ExitCode != 0
           && string.Join(' ', new[] { probe.Stdout, probe.Stderr }.Where(static line => !string.IsNullOrWhiteSpace(line)))
               .Contains("unrecognised disk label", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the device path of the newest partition on <paramref name="diskPath"/>
    /// (lsblk lists children in partition order), or null if it cannot be determined.
    /// </summary>
    private async Task<string?> ReadNewestPartitionPathAsync(string diskPath, CancellationToken ct)
    {
        try
        {
            var result = await _executor.ExecuteAsync("lsblk", $"--json -o NAME {Quote(diskPath)}", ct, throwOnNonZeroExit: false).ConfigureAwait(false);
            using var document = JsonDocument.Parse(result.Stdout);
            var devices = document.RootElement.GetProperty("blockdevices");
            if (devices.GetArrayLength() == 0 || !devices[0].TryGetProperty("children", out var children) || children.GetArrayLength() == 0)
            {
                return null;
            }

            var name = children[children.GetArrayLength() - 1].GetProperty("name").GetString();
            return string.IsNullOrWhiteSpace(name) ? null : Path.Combine("/dev", name);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or CommandExecutionException or PlatformException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<RaidResult> CreateRaidAsync(RaidLevel level, string[] diskPaths, CancellationToken ct)
    {
        if (diskPaths.Length == 0)
        {
            throw new ArgumentException("At least one disk is required.", nameof(diskPaths));
        }

        foreach (var diskPath in diskPaths)
        {
            ValidateDevicePath(diskPath);
            // Creating RAID writes a superblock to the member disks and erases their data: refuse disks that are already mounted
            // (defense in depth, not relying on the caller's confirmation), preventing an in-use system disk from being selected on a physical machine.
            await LinuxMountProbe.EnsureNotMountedAsync(_executor, diskPath, ct).ConfigureAwait(false);
        }

        var raidLevel = level switch
        {
            RaidLevel.Raid0 => "0",
            RaidLevel.Raid1 => "1",
            RaidLevel.Raid5 => "5",
            RaidLevel.Raid6 => "6",
            RaidLevel.Raid10 => "10",
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unsupported RAID level."),
        };

        var devices = string.Join(' ', diskPaths.Select(Quote));
        try
        {
            // Hard-coding /dev/md0 would collide with existing arrays, and a second RAID could never be created;
            // scan active arrays first and allocate the next available mdN device name.
            var device = await FindNextMdDeviceAsync(ct).ConfigureAwait(false);
            var result = await _executor.ExecuteAsync("mdadm", $"--create {device} --level={raidLevel} --raid-devices={diskPaths.Length} {devices}", ct).ConfigureAwait(false);
            await PersistRaidAssemblyAsync(ct).ConfigureAwait(false);
            return new RaidResult { Success = true, PoolId = device, Message = result.Stdout };
        }
        catch (Exception ex) when (ex is PlatformException or CommandExecutionException)
        {
            // Surface execution failures as a structured result instead of a 500.
            return new RaidResult { Success = false, ErrorCode = "RAID_CREATE_FAILED", Message = ex.Message };
        }
    }

    /// <summary>mdadm.conf writes are mutually exclusive: when creating RAIDs concurrently, read-modify-write must not interleave and drop lines.</summary>
    private static readonly SemaphoreSlim MdadmConfigGate = new(1, 1);

    /// <summary>
    /// Registers the newly created array in /etc/mdadm/mdadm.conf and refreshes initramfs so that the array is
    /// automatically assembled at boot (initramfs/system startup) without relying on the distro's default
    /// udev incremental scan. Persistence failures (e.g., read-only /etc, container environment) are only logged
    /// and do not mask the successful creation result.
    /// </summary>
    private async Task PersistRaidAssemblyAsync(CancellationToken ct)
    {
        const string configPath = "/etc/mdadm/mdadm.conf";
        try
        {
            var scan = await _executor.ExecuteAsync("mdadm", "--detail --scan", ct, throwOnNonZeroExit: false).ConfigureAwait(false);
            if (scan.ExitCode != 0 || string.IsNullOrWhiteSpace(scan.Stdout))
            {
                _logger.LogWarning("mdadm --detail --scan failed (exit {ExitCode}); RAID array will not be registered for auto-assembly.", scan.ExitCode);
                return;
            }

            await MdadmConfigGate.WaitAsync(ct).ConfigureAwait(false);
            var tempPath = configPath + ".tmp";
            try
            {
                var existing = File.Exists(configPath) ? await File.ReadAllTextAsync(configPath, ct).ConfigureAwait(false) : string.Empty;
                var lines = scan.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var missing = lines.Where(line => !existing.Contains(line, StringComparison.Ordinal)).ToArray();
                if (missing.Length > 0)
                {
                    // Atomic write via temp file + rename, avoiding a truncated mdadm.conf from a partial write.
                    var updated = existing.TrimEnd() + (existing.TrimEnd().Length > 0 ? "\n" : string.Empty) + string.Join('\n', missing) + "\n";
                    await File.WriteAllTextAsync(tempPath, updated, ct).ConfigureAwait(false);
                    File.Move(tempPath, configPath, overwrite: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Clean up leftover temporary files (best-effort), without masking the original exception.
                try
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                }
                catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(cleanupEx, "Unable to clean up stale mdadm.conf temporary file {TempPath}.", tempPath);
                }

                _logger.LogWarning(ex, "Unable to persist RAID array to {ConfigPath}; the array will rely on udev incremental assembly after reboot.", configPath);
            }
            finally
            {
                MdadmConfigGate.Release();
            }

            // Refresh initramfs so the array can be assembled as early as the initramfs phase (failure tolerated).
            await _executor.ExecuteAsync("update-initramfs", "-u", ct, throwOnNonZeroExit: false).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is PlatformException or CommandExecutionException)
        {
            _logger.LogWarning(ex, "Unable to run mdadm --detail --scan / update-initramfs; RAID array will rely on udev incremental assembly after reboot.");
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RaidMetrics>> ListRaidsAsync(CancellationToken ct)
    {
        var result = await _executor.ExecuteAsync("cat", "/proc/mdstat", ct, throwOnNonZeroExit: false).ConfigureAwait(false);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Stdout))
        {
            return [];
        }

        return LinuxProcParsers.ParseRaid(result.Stdout);
    }

    /// <inheritdoc />
    public async Task<SmartData> GetSmartDataAsync(string diskPath, CancellationToken ct)
    {
        ValidateDevicePath(diskPath);
        try
        {
            var result = await _executor.ExecuteAsync("smartctl", $"--json=c -a {Quote(diskPath)}", ct, throwOnNonZeroExit: false).ConfigureAwait(false);
            if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Stdout))
            {
                return new SmartData { DiskPath = diskPath, Health = "Unsupported", RawJson = result.Stdout };
            }

            using var document = JsonDocument.Parse(result.Stdout);
            var health = document.RootElement.TryGetProperty("smart_status", out var status) && status.TryGetProperty("passed", out var passed)
                ? passed.GetBoolean() ? "Passed" : "Failed"
                : "Unknown";
            int? temp = null;
            if (document.RootElement.TryGetProperty("temperature", out var tempElement) && tempElement.TryGetProperty("current", out var current) && current.TryGetInt32(out var value))
            {
                temp = value;
            }

            return new SmartData { DiskPath = diskPath, Health = health, TemperatureCelsius = temp, RawJson = result.Stdout };
        }
        catch (Exception ex) when (ex is PlatformException or CommandExecutionException or JsonException)
        {
            return new SmartData { DiskPath = diskPath, Health = "Unsupported", RawJson = ex.Message };
        }
    }

    /// <summary>
    /// Scans the active array names in /proc/mdstat and returns the next available /dev/mdN device name.
    /// mdstat lists only activated arrays; mdadm --create activates the new array immediately,
    /// so starting from "current max index + 1" avoids collisions with existing arrays.
    /// </summary>
    private async Task<string> FindNextMdDeviceAsync(CancellationToken ct)
    {
        var existing = await ListRaidsAsync(ct).ConfigureAwait(false);
        var maxIndex = existing
            .Select(raid => raid.Name)
            .Where(static name => name.StartsWith("md", StringComparison.Ordinal))
            .Select(static name => int.TryParse(name.AsSpan(2), out var index) ? index : -1)
            .DefaultIfEmpty(-1)
            .Max();
        return $"/dev/md{maxIndex + 1}";
    }

    /// <inheritdoc />
    public async Task<DeviceStatus> GetDeviceStatusAsync(string path, CancellationToken ct)
    {
        ValidateDevicePath(path);
        var result = await _executor.ExecuteAsync("lsblk", $"--json -b -o NAME,TYPE,FSTYPE,MOUNTPOINT,SIZE {Quote(path)}", ct, throwOnNonZeroExit: false).ConfigureAwait(false);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Stdout))
        {
            return new DeviceStatus { Path = path };
        }

        try
        {
            using var document = JsonDocument.Parse(result.Stdout);
            if (!document.RootElement.TryGetProperty("blockdevices", out var devices) || devices.GetArrayLength() == 0)
            {
                return new DeviceStatus { Path = path };
            }

            var block = devices[0];
            return new DeviceStatus
            {
                Path = path,
                Exists = true,
                FileSystem = GetString(block, "fstype"),
                MountPoint = GetString(block, "mountpoint"),
                SizeBytes = GetLong(block, "size"),
            };
        }
        catch (JsonException)
        {
            return new DeviceStatus { Path = path };
        }
    }

    /// <inheritdoc />
    public async Task WipeDiskAsync(string diskPath, CancellationToken ct)
    {
        ValidateDevicePath(diskPath);
        // Wiping the partition table is destructive: refuse disks that are mounted (defense in depth, not relying on the caller's confirmation).
        await LinuxMountProbe.EnsureNotMountedAsync(_executor, diskPath, ct).ConfigureAwait(false);
        await _executor.ExecuteAsync("wipefs", $"--all {Quote(diskPath)}", ct).ConfigureAwait(false);
    }

    private static void AddDisk(JsonElement block, List<DiskInfo> disks)
    {
        var type = GetString(block, "type");
        if (string.Equals(type, "disk", StringComparison.OrdinalIgnoreCase))
        {
            var mountPoint = GetString(block, "mountpoint");
            // When the whole-disk node has no mount point, aggregate the child partitions' mount points (e.g., a system disk's root partition mounted at
            // /dev/nvme0n1p2); otherwise the frontend cannot identify an "in use" whole disk and disable its selection.
            if (string.IsNullOrWhiteSpace(mountPoint) && block.TryGetProperty("children", out var childNodes))
            {
                mountPoint = FindMountedChild(childNodes);
            }

            disks.Add(new DiskInfo
            {
                Path = "/dev/" + GetString(block, "name"),
                Model = GetString(block, "model") ?? string.Empty,
                Serial = GetString(block, "serial") ?? string.Empty,
                SizeBytes = GetLong(block, "size"),
                InterfaceType = GetString(block, "tran") ?? string.Empty,
                IsSsd = !GetBool(block, "rota"),
                SmartStatus = "Unknown",
                TemperatureCelsius = 0,
                UsedPercent = ParsePercent(GetString(block, "fsuse%")),
                MountPoint = mountPoint,
            });
        }

        if (block.TryGetProperty("children", out var children))
        {
            foreach (var child in children.EnumerateArray())
            {
                AddDisk(child, disks);
            }
        }
    }

    /// <summary>Recursively finds the mount point of the first mounted child partition.</summary>
    private static string? FindMountedChild(JsonElement children)
    {
        foreach (var child in children.EnumerateArray())
        {
            var mountPoint = GetString(child, "mountpoint");
            if (!string.IsNullOrWhiteSpace(mountPoint))
            {
                return mountPoint;
            }

            if (child.TryGetProperty("children", out var nested) && FindMountedChild(nested) is { } nestedMount)
            {
                return nestedMount;
            }
        }

        return null;
    }

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var property) && property.ValueKind != JsonValueKind.Null ? property.ToString() : null;

    private static long GetLong(JsonElement element, string name)
        => element.TryGetProperty(name, out var property) && property.TryGetInt64(out var value) ? value : 0;

    private static bool GetBool(JsonElement element, string name)
        => element.TryGetProperty(name, out var property) && (property.ValueKind == JsonValueKind.True || (property.ValueKind == JsonValueKind.Number && property.GetInt32() != 0));

    private static double ParsePercent(string? value)
        => double.TryParse(value?.TrimEnd('%'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var percent) ? percent : 0;

    private static string ValidateFs(string fsType)
        => SafeTokenRegex().IsMatch(fsType) ? fsType : throw new ArgumentException("File system type contains illegal characters.", nameof(fsType));

    private static void ValidateDevicePath(string path)
    {
        if (!DevicePathRegex().IsMatch(path))
        {
            throw new ArgumentException("Unsafe device path.", nameof(path));
        }
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    [GeneratedRegex("^/dev/[A-Za-z0-9_./-]+$")]
    private static partial Regex DevicePathRegex();

    [GeneratedRegex("^[A-Za-z0-9_.+-]+$")]
    private static partial Regex SafeTokenRegex();
}
