using GNAS.Core;
using Microsoft.Extensions.DependencyInjection;

namespace GNAS.Modules.Host;

/// <summary>模块宿主依赖注入扩展。</summary>
public static class ModuleServiceExtensions
{
    /// <summary>注册模块宿主单例。</summary>
    public static IServiceCollection AddModuleHost(this IServiceCollection services)
    {
        services.AddSingleton<ModuleHost>();
        services.AddSingleton<IModuleHost>(sp => sp.GetRequiredService<ModuleHost>());
        return services;
    }
}
