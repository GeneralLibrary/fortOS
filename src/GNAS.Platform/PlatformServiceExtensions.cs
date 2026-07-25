using System.Runtime.InteropServices;
using GNAS.Platform.Linux;
using GNAS.Platform.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace GNAS.Platform;

/// <summary>
/// 平台服务自动注册扩展。
/// </summary>
public static class PlatformServiceExtensions
{
    /// <summary>根据当前操作系统注册平台服务。</summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合。</returns>
    /// <exception cref="PlatformNotSupportedException">当前平台不受支持。</exception>
    public static IServiceCollection AddPlatformServices(this IServiceCollection services)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            services.AddLinuxPlatform();
            if (RuntimeInformation.ProcessArchitecture is Architecture.Arm or Architecture.Arm64)
            {
                services.AddArmOptimization();
            }

            return services;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            services.AddWindowsPlatform();
            return services;
        }

        throw new PlatformNotSupportedException($"不支持的平台: {RuntimeInformation.OSDescription}");
    }
}
