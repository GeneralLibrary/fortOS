using Microsoft.Extensions.DependencyInjection;

namespace FortOS.Core;

/// <summary>
/// FortOS Core dependency injection extensions.
/// </summary>
public static class ServiceExtensions
{
    /// <summary>
    /// Register FortOS Core base services.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="dataRoot">Data root directory.</param>
    /// <param name="configPath">Configuration file path.</param>
    /// <returns>Service collection.</returns>
    public static IServiceCollection AddFortOSCore(this IServiceCollection services, string? dataRoot = null, string? configPath = null)
    {
        services.AddSingleton<IDatabaseProvider>(_ => new DatabaseProvider(dataRoot));
        services.AddSingleton<SqliteLeaseService>();
        services.AddSingleton<IFortOSConfiguration>(_ => new FortOSConfiguration(configPath));
        return services;
    }
}
