using System.Runtime.Versioning;
using GNAS.Core;
using Microsoft.Extensions.DependencyInjection;

namespace GNAS.Platform.Windows;

/// <summary>
/// Windows 平台服务注册扩展。
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsPlatformExtensions
{
    /// <summary>注册 Windows 平台服务。</summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddWindowsPlatform(this IServiceCollection services)
    {
        services.AddSingleton<IDiskManager, WindowsDiskManager>();
        services.AddSingleton<IFileSystem, WindowsFileSystem>();
        services.AddSingleton<IProcessManager, WindowsProcessManager>();
        services.AddSingleton<INetworkManager, WindowsNetworkManager>();
        services.AddSingleton<IUserAccount, WindowsUserAccount>();
        return services;
    }
}
