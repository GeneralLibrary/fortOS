using System.Runtime.Versioning;
using GNAS.Core;
using Microsoft.Extensions.DependencyInjection;

namespace GNAS.Platform.Linux;

/// <summary>
/// Linux 平台服务注册扩展。
/// </summary>
[SupportedOSPlatform("linux")]
public static class LinuxPlatformExtensions
{
    /// <summary>注册 Linux 平台服务。</summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddLinuxPlatform(this IServiceCollection services)
    {
        services.AddSingleton<IDiskManager, LinuxDiskManager>();
        services.AddSingleton<IFileSystem, LinuxFileSystem>();
        services.AddSingleton<IProcessManager, LinuxProcessManager>();
        services.AddSingleton<INetworkManager, LinuxNetworkManager>();
        services.AddSingleton<IUserAccount, LinuxUserAccount>();
        services.AddSingleton<ArchitectureDetector>();
        return services;
    }
}
