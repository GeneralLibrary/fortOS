using System.Runtime.Versioning;
using FortOS.Core;
using FortOS.Platform.Linux.Monitoring;
using Microsoft.Extensions.DependencyInjection;

namespace FortOS.Platform.Linux;

/// <summary>
/// Linux platform service registration extensions.
/// </summary>
[SupportedOSPlatform("linux")]
public static class LinuxPlatformExtensions
{
    /// <summary>Registers Linux platform services.</summary>
    /// <param name="services">Service collection.</param>
    /// <returns>Service collection.</returns>
    public static IServiceCollection AddLinuxPlatform(this IServiceCollection services)
    {
        services.AddSingleton<IDiskManager, LinuxDiskManager>();
        services.AddSingleton<IFileSystem, LinuxFileSystem>();
        services.AddSingleton<IProcessManager, LinuxProcessManager>();
        services.AddSingleton<INetworkManager, LinuxNetworkManager>();
        services.AddSingleton<ISystemMetricsCollector, LinuxSystemMetricsCollector>();
        services.AddSingleton<IUserAccount, LinuxUserAccount>();
        services.AddSingleton<ArchitectureDetector>();
        return services;
    }
}
