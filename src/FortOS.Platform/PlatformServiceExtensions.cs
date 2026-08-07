using System.Runtime.InteropServices;
using FortOS.Platform.Linux;
using Microsoft.Extensions.DependencyInjection;

namespace FortOS.Platform;

/// <summary>
/// Linux platform service registration extensions.
/// </summary>
public static class PlatformServiceExtensions
{
    /// <summary>Registers Linux platform services and refuses to start on other operating systems.</summary>
    /// <param name="services">Service collection.</param>
    /// <returns>Service collection.</returns>
    /// <exception cref="PlatformNotSupportedException">Current platform is not supported.</exception>
    public static IServiceCollection AddFortOSPlatform(this IServiceCollection services)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException(
                $"FortOS only supports Linux, current platform is {RuntimeInformation.OSDescription}.");
        }

        services.AddLinuxPlatform();
        if (RuntimeInformation.ProcessArchitecture is Architecture.Arm or Architecture.Arm64)
        {
            services.AddArmOptimization();
        }

        return services;
    }
}
