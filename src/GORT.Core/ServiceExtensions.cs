using Microsoft.Extensions.DependencyInjection;

namespace GORT.Core;

/// <summary>
/// GORT Core dependency injection extensions.
/// </summary>
public static class ServiceExtensions
{
    /// <summary>
    /// Register GORT Core base services.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="dataRoot">Data root directory.</param>
    /// <param name="configPath">Configuration file path.</param>
    /// <returns>Service collection.</returns>
    public static IServiceCollection AddGortCore(this IServiceCollection services, string? dataRoot = null, string? configPath = null)
    {
        services.AddSingleton<IDatabaseProvider>(_ => new DatabaseProvider(dataRoot));
        services.AddSingleton<SqliteLeaseService>();
        services.AddSingleton<IGortConfiguration>(_ => new GortConfiguration(configPath));
        return services;
    }
}
