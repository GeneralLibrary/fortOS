using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;

namespace GORT.Platform.Linux;

/// <summary>
/// ARM optimization service registration extensions.
/// </summary>
[SupportedOSPlatform("linux")]
public static class ArmPlatformExtensions
{
    /// <summary>Registers ARM hardware optimization services.</summary>
    /// <param name="services">Service collection.</param>
    /// <returns>Service collection.</returns>
    public static IServiceCollection AddArmOptimization(this IServiceCollection services)
    {
        services.AddSingleton<ArmHardwareOptimizer>();
        return services;
    }
}
