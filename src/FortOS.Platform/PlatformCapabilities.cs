using System.Diagnostics;

namespace FortOS.Platform;

/// <summary>
/// Platform capability detection result.
/// </summary>
public static class PlatformCapabilities
{
    private static readonly Lazy<bool> SupportsDockerValue = new(() => HasBinary("docker") && Probe("docker", "--version"));
    private static readonly Lazy<bool> SupportsSmartMonitoringValue = new(() => HasBinary("smartctl") || File.Exists("/usr/sbin/smartctl") || File.Exists("/sbin/smartctl"));
    private static readonly Lazy<bool> SupportsHardwareRaidValue = new(() => HasBinary("mdadm") || File.Exists("/usr/sbin/mdadm") || File.Exists("/sbin/mdadm"));
    private static readonly Lazy<bool> SupportsTpmValue = new(() => File.Exists("/dev/tpm0") || File.Exists("/dev/tpmrm0"));
    private static readonly Lazy<bool> SupportsZfsValue = new(() => HasBinary("zfs"));
    private static readonly Lazy<long> TotalMemoryBytesValue = new(GetTotalMemoryBytesSafe);

    /// <summary>Whether Docker is supported.</summary>
    public static bool SupportsDocker => SupportsDockerValue.Value;

    /// <summary>Whether SMART monitoring is supported.</summary>
    public static bool SupportsSmartMonitoring => SupportsSmartMonitoringValue.Value;

    /// <summary>Whether hardware RAID is supported.</summary>
    public static bool SupportsHardwareRaid => SupportsHardwareRaidValue.Value;

    /// <summary>Whether TPM is supported.</summary>
    public static bool SupportsTpm => SupportsTpmValue.Value;

    /// <summary>Whether ZFS is supported.</summary>
    public static bool SupportsZfs => SupportsZfsValue.Value;

    /// <summary>Total memory in bytes.</summary>
    public static long TotalMemoryBytes => TotalMemoryBytesValue.Value;

    /// <summary>Number of CPU cores.</summary>
    public static int CpuCores => Environment.ProcessorCount;

    private static bool HasBinary(string name)
    {
        try
        {
            var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            return paths.Any(p => File.Exists(Path.Combine(p, name)));
        }
        catch
        {
            return false;
        }
    }

    private static bool Probe(string fileName, string arguments)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var process = Process.Start(new ProcessStartInfo { FileName = fileName, Arguments = arguments, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true });
            if (process is null) return false;
            process.WaitForExit(3000);
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static long GetTotalMemoryBytesSafe()
    {
        try
        {
            if (File.Exists("/proc/meminfo"))
            {
                var line = File.ReadLines("/proc/meminfo").FirstOrDefault(l => l.StartsWith("MemTotal:", StringComparison.Ordinal));
                var parts = line?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts is { Length: >= 2 } && long.TryParse(parts[1], out var kb)) return kb * 1024;
            }

            return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        }
        catch
        {
            return 0;
        }
    }
}
