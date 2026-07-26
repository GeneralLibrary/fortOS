using System.Runtime.InteropServices;
using GNAS.Platform.Linux;
using Microsoft.Extensions.DependencyInjection;

namespace GNAS.Platform;

/// <summary>
/// Linux platform service registration extensions.
/// </summary>
public static class PlatformServiceExtensions
{
    /// <summary>Registers Linux platform services and refuses to start on other operating systems.</summary>
    /// <param name="services">Service collection.</param>
    /// <returns>Service collection.</returns>
    /// <exception cref="PlatformNotSupportedException">Current platform is not supported.</exception>
    public static IServiceCollection AddPlatformServices(this IServiceCollection services)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException(
                $"GNAS only supports Linux, current platform is {RuntimeInformation.OSDescription}.");
        }

        services.AddLinuxPlatform();
        if (RuntimeInformation.ProcessArchitecture is Architecture.Arm or Architecture.Arm64)
        {
            services.AddArmOptimization();
        }

        return services;
    }
}
