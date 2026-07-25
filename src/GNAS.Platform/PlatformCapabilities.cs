using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GNAS.Platform;

/// <summary>
/// 平台能力探测结果。
/// </summary>
public static class PlatformCapabilities
{
    private static readonly Lazy<bool> SupportsDockerValue = new(() => HasBinary("docker") && Probe("docker", "--version"));
    private static readonly Lazy<bool> SupportsSmartMonitoringValue = new(() => HasBinary("smartctl") || File.Exists("/usr/sbin/smartctl") || File.Exists("/sbin/smartctl"));
    private static readonly Lazy<bool> SupportsHardwareRaidValue = new(() => RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? HasBinary("mdadm") : ProbePowerShell("Get-Command Get-StoragePool"));
    private static readonly Lazy<bool> SupportsTpmValue = new(() => RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? File.Exists("/dev/tpm0") : ProbePowerShell("Get-Tpm"));
    private static readonly Lazy<bool> SupportsZfsValue = new(() => HasBinary("zfs"));
    private static readonly Lazy<long> TotalMemoryBytesValue = new(GetTotalMemoryBytesSafe);

    /// <summary>是否支持 Docker。</summary>
    public static bool SupportsDocker => SupportsDockerValue.Value;

    /// <summary>是否支持 SMART 监控。</summary>
    public static bool SupportsSmartMonitoring => SupportsSmartMonitoringValue.Value;

    /// <summary>是否支持硬件 RAID。</summary>
    public static bool SupportsHardwareRaid => SupportsHardwareRaidValue.Value;

    /// <summary>是否支持 TPM。</summary>
    public static bool SupportsTpm => SupportsTpmValue.Value;

    /// <summary>是否支持 ZFS。</summary>
    public static bool SupportsZfs => SupportsZfsValue.Value;

    /// <summary>总内存字节数。</summary>
    public static long TotalMemoryBytes => TotalMemoryBytesValue.Value;

    /// <summary>CPU 核心数。</summary>
    public static int CpuCores => Environment.ProcessorCount;

    private static bool HasBinary(string name)
    {
        try
        {
            var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            return paths.Any(p => File.Exists(Path.Combine(p, name)) || File.Exists(Path.Combine(p, name + ".exe")));
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

    private static bool ProbePowerShell(string script)
    {
        try
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && Probe("powershell", $"-NoProfile -NonInteractive -Command \"$ErrorActionPreference='Stop'; {script}\"");
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
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && File.Exists("/proc/meminfo"))
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
