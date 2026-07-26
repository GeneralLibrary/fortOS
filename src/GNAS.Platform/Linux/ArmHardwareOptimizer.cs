using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using GNAS.Core;
using GNAS.Platform.Execution;
using Microsoft.Extensions.Logging;

namespace GNAS.Platform.Linux;

/// <summary>
/// ARM hardware optimizer.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class ArmHardwareOptimizer
{
    private readonly CommandExecutor _executor;

    /// <summary>Initializes the ARM hardware optimizer.</summary>
    /// <param name="logger">Logger.</param>
    public ArmHardwareOptimizer(ILogger<ArmHardwareOptimizer> logger)
    {
        _executor = new CommandExecutor(logger);
    }

    /// <summary>Detects whether the root file system is on an SD card.</summary>
    /// <returns>True if the root device is removable.</returns>
    public bool IsRootOnSdCard() => GetRootBlockDevice() is { } device && IsRemovable(device);

    /// <summary>Detects whether the root file system is on an SSD.</summary>
    /// <returns>True if the root device is non-rotational and non-removable.</returns>
    public bool IsRootOnSsd() => GetRootBlockDevice() is { } device && !IsRemovable(device) && !IsRotational(device);

    /// <summary>Computes the recommended Docker memory limit.</summary>
    /// <param name="reserveBytes">Bytes to reserve for the system.</param>
    /// <returns>Recommended limit in bytes.</returns>
    public long GetRecommendedDockerMemoryLimitBytes(long reserveBytes = 512L * 1024 * 1024)
    {
        var total = GetTotalMemoryBytes();
        if (total <= 0) return 0;
        return Math.Max(total / 2, total - reserveBytes);
    }

    /// <summary>Reads the hardware temperature.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Temperature in Celsius, or null if unreadable.</returns>
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
