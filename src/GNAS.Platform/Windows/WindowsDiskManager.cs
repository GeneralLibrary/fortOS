using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using GNAS.Core;
using GNAS.Platform.Execution;
using Microsoft.Extensions.Logging;

namespace GNAS.Platform.Windows;

/// <summary>
/// Windows 磁盘管理器。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsDiskManager : IDiskManager
{
    private readonly CommandExecutor _executor;

    /// <summary>初始化 Windows 磁盘管理器。</summary>
    /// <param name="logger">日志记录器。</param>
    public WindowsDiskManager(ILogger<WindowsDiskManager> logger) => _executor = new CommandExecutor(logger);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DiskInfo>> ListDisksAsync(CancellationToken ct)
    {
        var script = "$ErrorActionPreference='Stop'; Get-Disk | Select-Object Number,FriendlyName,SerialNumber,Size,BusType,HealthStatus | ConvertTo-Json -Depth 4";
        var result = await PowerShellAsync(script, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(NormalizeJsonArray(result.Stdout));
        return doc.RootElement.EnumerateArray().Select(e => new DiskInfo
        {
            Path = "\\\\.\\PhysicalDrive" + GetString(e, "Number"),
            Model = GetString(e, "FriendlyName") ?? string.Empty,
            Serial = GetString(e, "SerialNumber") ?? string.Empty,
            SizeBytes = GetLong(e, "Size"),
            InterfaceType = GetString(e, "BusType") ?? string.Empty,
            IsSsd = false,
            SmartStatus = GetString(e, "HealthStatus") ?? "Unknown",
            TemperatureCelsius = 0,
            UsedPercent = 0,
        }).ToArray();
    }

    /// <inheritdoc />
    public async Task<DiskInfo?> GetDiskAsync(string path, CancellationToken ct) => (await ListDisksAsync(ct).ConfigureAwait(false)).FirstOrDefault(d => d.Path == path);

    /// <inheritdoc />
    public async Task<PartitionResult> CreatePartitionAsync(string diskPath, PartitionSpec spec, CancellationToken ct)
    {
        var number = ParseDiskNumber(diskPath);
        var size = spec.SizeBytes.HasValue ? $" -Size {spec.SizeBytes.Value}" : " -UseMaximumSize";
        var script = $"$ErrorActionPreference='Stop'; New-Partition -DiskNumber {number}{size} | Format-Volume -Confirm:$false";
        var result = await PowerShellAsync(script, ct).ConfigureAwait(false);
        return new PartitionResult { Success = true, PartitionPath = diskPath, Message = result.Stdout };
    }

    /// <inheritdoc />
    public async Task<RaidResult> CreateRaidAsync(RaidLevel level, string[] diskPaths, CancellationToken ct)
    {
        var resiliency = level switch { RaidLevel.Raid0 => "Simple", RaidLevel.Raid1 => "Mirror", RaidLevel.Raid5 => "Parity", _ => "Simple" };
        var script = $"$ErrorActionPreference='Stop'; New-StoragePool -FriendlyName GNASPool -StorageSubSystemFriendlyName '*Storage Spaces*' -PhysicalDisks (Get-PhysicalDisk -CanPool $true); New-VirtualDisk -StoragePoolFriendlyName GNASPool -FriendlyName GNASVirtualDisk -ResiliencySettingName {resiliency} -UseMaximumSize";
        var result = await PowerShellAsync(script, ct).ConfigureAwait(false);
        return new RaidResult { Success = true, PoolId = "GNASPool", Message = result.Stdout };
    }

    /// <inheritdoc />
    public async Task<SmartData> GetSmartDataAsync(string diskPath, CancellationToken ct)
    {
        try
        {
            var number = ParseDiskNumber(diskPath);
            var result = await PowerShellAsync($"$ErrorActionPreference='Stop'; Get-PhysicalDisk | Select-Object FriendlyName,HealthStatus,OperationalStatus | ConvertTo-Json -Depth 4", ct).ConfigureAwait(false);
            return new SmartData { DiskPath = diskPath, Health = string.IsNullOrWhiteSpace(result.Stdout) ? "Unknown" : "Unknown", RawJson = result.Stdout };
        }
        catch
        {
            return new SmartData { DiskPath = diskPath, Health = "Unsupported" };
        }
    }

    /// <inheritdoc />
    public async Task WipeDiskAsync(string diskPath, CancellationToken ct)
    {
        var number = ParseDiskNumber(diskPath);
        await PowerShellAsync($"$ErrorActionPreference='Stop'; Clear-Disk -Number {number} -RemoveData -Confirm:$false", ct).ConfigureAwait(false);
    }

    private Task<CommandResult> PowerShellAsync(string script, CancellationToken ct) => _executor.ExecuteAsync("powershell", $"-NoProfile -NonInteractive -Command {Quote(script)}", ct);

    private static int ParseDiskNumber(string diskPath)
    {
        var match = DiskPathRegex().Match(diskPath);
        return match.Success ? int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : throw new ArgumentException("磁盘路径无效。", nameof(diskPath));
    }

    private static string NormalizeJsonArray(string json) => string.IsNullOrWhiteSpace(json) ? "[]" : json.TrimStart().StartsWith('[') ? json : "[" + json + "]";
    private static string? GetString(JsonElement e, string n) => e.TryGetProperty(n, out var p) && p.ValueKind != JsonValueKind.Null ? p.ToString() : null;
    private static long GetLong(JsonElement e, string n) => e.TryGetProperty(n, out var p) && p.TryGetInt64(out var v) ? v : 0;
    private static string Quote(string value) => "\"" + value.Replace("\"", "`\"", StringComparison.Ordinal) + "\"";

    [GeneratedRegex("PhysicalDrive(\\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex DiskPathRegex();
}
