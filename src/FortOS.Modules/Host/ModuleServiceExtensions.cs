using FortOS.Core;
using Microsoft.Extensions.DependencyInjection;

namespace FortOS.Modules.Host;

/// <summary>Module host dependency injection extensions.</summary>
public static class ModuleServiceExtensions
{
    /// <summary>Register module host singleton.</summary>
    public static IServiceCollection AddFortOSModuleHost(this IServiceCollection services)
    {
        services.AddSingleton<ModuleHost>();
        services.AddSingleton<IModuleHost>(sp => sp.GetRequiredService<ModuleHost>());
        return services;
    }
}
