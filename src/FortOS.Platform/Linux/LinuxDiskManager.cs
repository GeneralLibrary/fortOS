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

    /// <summary>Initializes the Linux disk manager.</summary>
    /// <param name="logger">Logger.</param>
    public LinuxDiskManager(ILogger<LinuxDiskManager> logger)
    {
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

        var start = spec.StartBytes.HasValue ? $"{spec.StartBytes.Value}B" : "1MiB";
        var end = spec.SizeBytes.HasValue ? $"{spec.StartBytes!.Value + spec.SizeBytes.Value}B" : "100%";
        var fsType = string.IsNullOrWhiteSpace(spec.FileSystem) ? "ext4" : ValidateFs(spec.FileSystem);
        var args = $"--script {Quote(diskPath)} mklabel gpt mkpart {Quote(spec.Name)} {Quote(fsType)} {Quote(start)} {Quote(end)}";
        await _executor.ExecuteAsync("parted", args, ct).ConfigureAwait(false);

        // parted does not echo the created partition's device path; read it back with lsblk so
        // callers receive a usable path (e.g. /dev/sda1) instead of the disk itself.
        var partitionPath = await ReadNewestPartitionPathAsync(diskPath, ct).ConfigureAwait(false);
        return new PartitionResult { Success = true, PartitionPath = partitionPath, Message = "Partition created." };
    }

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
            var result = await _executor.ExecuteAsync("mdadm", $"--create /dev/md0 --level={raidLevel} --raid-devices={diskPaths.Length} {devices}", ct).ConfigureAwait(false);
            return new RaidResult { Success = true, PoolId = "/dev/md0", Message = result.Stdout };
        }
        catch (Exception ex) when (ex is PlatformException or CommandExecutionException)
        {
            // Surface execution failures as a structured result instead of a 500.
            return new RaidResult { Success = false, ErrorCode = "RAID_CREATE_FAILED", Message = ex.Message };
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

    /// <inheritdoc />
    public Task WipeDiskAsync(string diskPath, CancellationToken ct)
    {
        ValidateDevicePath(diskPath);
        return _executor.ExecuteAsync("wipefs", $"--all {Quote(diskPath)}", ct);
    }

    private static void AddDisk(JsonElement block, List<DiskInfo> disks)
    {
        var type = GetString(block, "type");
        if (string.Equals(type, "disk", StringComparison.OrdinalIgnoreCase))
        {
            var mountPoint = GetString(block, "mountpoint");
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
