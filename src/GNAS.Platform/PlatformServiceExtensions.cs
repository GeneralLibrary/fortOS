using System.Runtime.InteropServices;
using GNAS.Platform.Linux;
using Microsoft.Extensions.DependencyInjection;

namespace GNAS.Platform;

/// <summary>
/// Linux 平台服务注册扩展。
/// </summary>
public static class PlatformServiceExtensions
{
    /// <summary>注册 Linux 平台服务，并拒绝在其他操作系统上启动。</summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合。</returns>
    /// <exception cref="PlatformNotSupportedException">当前平台不受支持。</exception>
    public static IServiceCollection AddPlatformServices(this IServiceCollection services)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException(
                $"GNAS 仅支持 Linux，当前平台为 {RuntimeInformation.OSDescription}。");
        }

        services.AddLinuxPlatform();
        if (RuntimeInformation.ProcessArchitecture is Architecture.Arm or Architecture.Arm64)
        {
            services.AddArmOptimization();
        }

        return services;
    }
}
