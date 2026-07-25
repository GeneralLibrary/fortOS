using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using GNAS.Core;
using GNAS.Platform.Execution;
using Microsoft.Extensions.Logging;

namespace GNAS.Platform.Linux;

/// <summary>
/// ARM 硬件优化器。
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class ArmHardwareOptimizer
{
    private readonly CommandExecutor _executor;

    /// <summary>初始化 ARM 硬件优化器。</summary>
    /// <param name="logger">日志记录器。</param>
    public ArmHardwareOptimizer(ILogger<ArmHardwareOptimizer> logger)
    {
        _executor = new CommandExecutor(logger);
    }

    /// <summary>检测根文件系统是否位于 SD 卡。</summary>
    /// <returns>如果根设备可移动则返回 true。</returns>
    public bool IsRootOnSdCard() => GetRootBlockDevice() is { } device && IsRemovable(device);

    /// <summary>检测根文件系统是否位于 SSD。</summary>
    /// <returns>如果根设备不可旋转且不可移动则返回 true。</returns>
    public bool IsRootOnSsd() => GetRootBlockDevice() is { } device && !IsRemovable(device) && !IsRotational(device);

    /// <summary>计算建议的 Docker 内存限制。</summary>
    /// <param name="reserveBytes">保留给系统的字节数。</param>
    /// <returns>建议限制字节数。</returns>
    public long GetRecommendedDockerMemoryLimitBytes(long reserveBytes = 512L * 1024 * 1024)
    {
        var total = GetTotalMemoryBytes();
        if (total <= 0) return 0;
        return Math.Max(total / 2, total - reserveBytes);
    }

    /// <summary>读取硬件温度。</summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>温度摄氏度，无法读取时为空。</returns>
    public async Task<double?> ReadTemperatureCelsiusAsync(CancellationToken ct)
    {
        CommandResult? vcgen = null;
        try
        {
            vcgen = await _executor.ExecuteAsync("vcgencmd", "measure_temp", ct, timeout: TimeSpan.FromSeconds(3), throwOnNonZeroExit: false).ConfigureAwait(false);
        }
        catch
        {
        }

        if (vcgen?.ExitCode == 0 && TryParseVcgencmd(vcgen.Stdout, out var value))
        {
            return value;
        }

        try
        {
            var raw = await File.ReadAllTextAsync("/sys/class/thermal/thermal_zone0/temp", ct).ConfigureAwait(false);
            return double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var milli) ? milli / 1000.0 : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryParseVcgencmd(string text, out double value)
    {
        value = 0;
        var start = text.IndexOf('=', StringComparison.Ordinal);
        var end = text.IndexOf('\'', StringComparison.Ordinal);
        return start >= 0 && end > start && double.TryParse(text[(start + 1)..end], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string? GetRootBlockDevice()
    {
        try
        {
            var line = File.ReadLines("/proc/mounts").FirstOrDefault(l => l.Split(' ')[1] == "/");
            var source = line?.Split(' ')[0];
            if (string.IsNullOrWhiteSpace(source) || !source.StartsWith("/dev/", StringComparison.Ordinal)) return null;
            var name = Path.GetFileName(source);
            while (name.Length > 0 && char.IsDigit(name[^1])) name = name[..^1];
            if (name.EndsWith('p')) name = name[..^1];
            return name;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsRemovable(string device) => ReadSysFlag(device, "removable") == 1;

    private static bool IsRotational(string device) => ReadSysFlag(device, "queue/rotational") == 1;

    private static int ReadSysFlag(string device, string file)
    {
        try
        {
            return int.TryParse(File.ReadAllText($"/sys/block/{device}/{file}").Trim(), out var value) ? value : -1;
        }
        catch
        {
            return -1;
        }
    }

    private static long GetTotalMemoryBytes()
    {
        try
        {
            var line = File.ReadLines("/proc/meminfo").FirstOrDefault(l => l.StartsWith("MemTotal:", StringComparison.Ordinal));
            var parts = line?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts is { Length: >= 2 } && long.TryParse(parts[1], out var kb) ? kb * 1024 : 0;
        }
        catch
        {
            return 0;
        }
    }
}
