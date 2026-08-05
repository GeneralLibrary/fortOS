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

    /// <summary>按磁盘路径串行化分区操作：probe→mklabel→mkpart 窗口无锁时，两个并发操作
    /// 作用于全新盘会让后者的 mklabel gpt 销毁前者刚建的分区表。</summary>
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

        // 同一磁盘的分区操作整体串行（probe→mklabel→mkpart），防止并发建分区互相清空分区表。
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

        // 仅在磁盘尚无分区表时初始化 GPT 标签。绝不能无条件 mklabel gpt，
        // 否则每次「添加分区」都会先销毁整盘现有分区表（数据丢失）。
        await EnsureDiskLabelAsync(diskPath, ct).ConfigureAwait(false);

        var args = $"--script {Quote(diskPath)} mkpart {Quote(spec.Name)} {Quote(fsType)} {Quote(start)} {Quote(end)}";
        await _executor.ExecuteAsync("parted", args, ct).ConfigureAwait(false);

        // parted does not echo the created partition's device path; read it back with lsblk so
        // callers receive a usable path (e.g. /dev/sda1) instead of the disk itself.
        var partitionPath = await ReadNewestPartitionPathAsync(diskPath, ct).ConfigureAwait(false);
        return new PartitionResult { Success = true, PartitionPath = partitionPath, Message = "Partition created." };
    }

    /// <summary>
    /// 确保磁盘带有分区表。已存在有效标签时保持原样；仅当 parted 明确报告
    /// 「unrecognised disk label」（全新盘）时才写入 GPT 标签。其他错误
    /// （设备不存在、IO 失败等）直接抛出，避免对异常设备执行破坏性操作。
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
    /// 判定磁盘是否需要初始化 GPT 标签：仅当 parted 明确报告「unrecognised disk label」
    /// （全新盘）时才返回 true；探测失败且原因不明（设备不存在等）返回 false，
    /// 调用方将拒绝破坏性操作。抽为纯函数便于回归测试锁定 F1 数据保护语义。
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
            // 创建 RAID 会向成员盘写入超级块并抹除盘上数据:拒绝已挂载的磁盘
            // (纵深防御,不依赖调用方确认),防止物理机上误选正在使用的系统盘。
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
            // 固定命名 /dev/md0 会与既有阵列冲突，且第二个 RAID 永远无法创建；
            // 先扫描活动阵列，分配下一个可用的 mdN 设备名。
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

    /// <summary>mdadm.conf 写入互斥:并发创建 RAID 时,read-modify-write 不能交错丢行。</summary>
    private static readonly SemaphoreSlim MdadmConfigGate = new(1, 1);

    /// <summary>
    /// 将新创建的阵列注册到 /etc/mdadm/mdadm.conf 并刷新 initramfs，保证重启后
    /// initramfs/系统启动阶段能自动装配（不依赖 udev 增量扫描的发行版默认行为）。
    /// 持久化失败（如只读 /etc、容器环境）只记日志，不掩盖创建成功的结果。
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
                    // 临时文件 + rename 原子写入，避免写一半截断 mdadm.conf。
                    var updated = existing.TrimEnd() + (existing.TrimEnd().Length > 0 ? "\n" : string.Empty) + string.Join('\n', missing) + "\n";
                    await File.WriteAllTextAsync(tempPath, updated, ct).ConfigureAwait(false);
                    File.Move(tempPath, configPath, overwrite: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 清理残留临时文件（best-effort），不掩盖原始异常。
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

            // 刷新 initramfs，让阵列在 initramfs 阶段即可装配（失败容忍）。
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
    /// 扫描 /proc/mdstat 中活动阵列的名称，返回下一个可用的 /dev/mdN 设备名。
    /// mdstat 只列出已激活的阵列；mdadm --create 会立即激活新阵列，
    /// 因此以「当前最大编号 + 1」为起点即可避免与既有阵列冲突。
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
        // 擦除分区表属于破坏性操作：拒绝已挂载的磁盘（纵深防御，不依赖调用方确认）。
        await LinuxMountProbe.EnsureNotMountedAsync(_executor, diskPath, ct).ConfigureAwait(false);
        await _executor.ExecuteAsync("wipefs", $"--all {Quote(diskPath)}", ct).ConfigureAwait(false);
    }

    private static void AddDisk(JsonElement block, List<DiskInfo> disks)
    {
        var type = GetString(block, "type");
        if (string.Equals(type, "disk", StringComparison.OrdinalIgnoreCase))
        {
            var mountPoint = GetString(block, "mountpoint");
            // 整盘节点无挂载点时,聚合子分区的挂载点(典型如系统盘根分区挂载在
            // /dev/nvme0n1p2),否则前端无法识别「使用中」的整盘并禁选。
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

    /// <summary>递归查找第一个已挂载的子分区挂载点。</summary>
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
