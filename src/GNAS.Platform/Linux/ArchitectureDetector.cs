using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace GNAS.Platform.Linux;

/// <summary>
/// Linux CPU 架构检测器。
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class ArchitectureDetector
{
    private readonly ILogger<ArchitectureDetector> _logger;

    /// <summary>初始化架构检测器。</summary>
    /// <param name="logger">日志记录器。</param>
    public ArchitectureDetector(ILogger<ArchitectureDetector> logger)
    {
        _logger = logger;
    }

    /// <summary>当前进程架构。</summary>
    public Architecture ProcessArchitecture => RuntimeInformation.ProcessArchitecture;

    /// <summary>是否为 ARM 架构。</summary>
    public bool IsArm => ProcessArchitecture is Architecture.Arm or Architecture.Arm64;

    /// <summary>是否为树莓派设备。</summary>
    public bool IsRaspberryPi => DetectRaspberryPi();

    /// <summary>获取硬件摘要。</summary>
    /// <returns>硬件架构摘要。</returns>
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
            _logger.LogDebug(ex, "树莓派检测失败。");
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
/// 硬件架构信息。
/// </summary>
public sealed record HardwareArchitectureInfo
{
    /// <summary>架构名称。</summary>
    public required string Architecture { get; init; }
    /// <summary>是否为 ARM。</summary>
    public bool IsArm { get; init; }
    /// <summary>是否为树莓派。</summary>
    public bool IsRaspberryPi { get; init; }
    /// <summary>CPU 信息。</summary>
    public string? CpuInfo { get; init; }
    /// <summary>设备型号。</summary>
    public string? DeviceModel { get; init; }
}
