using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;

namespace GNAS.Platform.Linux;

/// <summary>
/// ARM 优化服务注册扩展。
/// </summary>
[SupportedOSPlatform("linux")]
public static class ArmPlatformExtensions
{
    /// <summary>注册 ARM 硬件优化服务。</summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddArmOptimization(this IServiceCollection services)
    {
        services.AddSingleton<ArmHardwareOptimizer>();
        return services;
    }
}
