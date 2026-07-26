using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace GNAS.Platform.Linux;

/// <summary>
/// Linux CPU architecture detector.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class ArchitectureDetector
{
    private readonly ILogger<ArchitectureDetector> _logger;

    /// <summary>Initializes the architecture detector.</summary>
    /// <param name="logger">Logger.</param>
    public ArchitectureDetector(ILogger<ArchitectureDetector> logger)
    {
        _logger = logger;
    }

    /// <summary>Current process architecture.</summary>
    public Architecture ProcessArchitecture => RuntimeInformation.ProcessArchitecture;

    /// <summary>Whether it is an ARM architecture.</summary>
    public bool IsArm => ProcessArchitecture is Architecture.Arm or Architecture.Arm64;

    /// <summary>Whether it is a Raspberry Pi device.</summary>
    public bool IsRaspberryPi => DetectRaspberryPi();

    /// <summary>Gets the hardware summary.</summary>
    /// <returns>Hardware architecture summary.</returns>
    public HardwareArchitectureInfo GetInfo()
        => new() { Architecture = ProcessArchitecture.ToString(), IsArm = IsArm, IsRaspberryPi = IsRaspberryPi, CpuInfo = ReadSafe("/proc/cpuinfo"), DeviceModel = ReadSafe("/proc/device-tree/model") };

    private bool DetectRaspberryPi()
    {
        try
        {
            var text = (ReadSafe("/proc/cpuinfo") + "\n" + ReadSafe("/proc/device-tree/model")).ToLowerInvariant();
            return text.Contains("raspberry pi", StringComparison.Ordinal) || text.Contains("bcm", StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Raspberry Pi detection failed.");
            return false;
        }
    }

    private static string? ReadSafe(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path).Trim('\0', '\r', '\n', ' ') : null; }
        catch { return null; }
    }
}

/// <summary>
/// Hardware architecture information.
/// </summary>
public sealed record HardwareArchitectureInfo
{
    /// <summary>Architecture name.</summary>
    public required string Architecture { get; init; }
    /// <summary>Whether it is ARM.</summary>
    public bool IsArm { get; init; }
    /// <summary>Whether it is Raspberry Pi.</summary>
    public bool IsRaspberryPi { get; init; }
    /// <summary>CPU information.</summary>
    public string? CpuInfo { get; init; }
    /// <summary>Device model.</summary>
    public string? DeviceModel { get; init; }
}
