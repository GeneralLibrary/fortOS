using GORT.Core;
using GORT.ServiceBus.Events;
using GORT.ServiceBus.Health;
using GORT.ServiceBus.Registry;
using GORT.ServiceBus.Supervisor;
using Microsoft.Extensions.DependencyInjection;

namespace GORT.ServiceBus;

/// <summary>
/// Service bus dependency injection extensions.
/// </summary>
public static class ServiceBusExtensions
{
    /// <summary>
    /// Registers service bus components.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>Service collection.</returns>
    public static IServiceCollection AddServiceBus(this IServiceCollection services)
    {
        services.AddSingleton<IEventBus, EventBus>();
        services.AddSingleton<IServiceRegistry, ServiceRegistry>();
        services.AddSingleton<ServiceSupervisor>();
        services.AddSingleton<IServiceSupervisor>(sp => sp.GetRequiredService<ServiceSupervisor>());
        services.AddHostedService(sp => sp.GetRequiredService<ServiceSupervisor>());
        services.AddSingleton<HealthMonitor>();
        services.AddSingleton<IHealthMonitor>(sp => sp.GetRequiredService<HealthMonitor>());
        services.AddHostedService(sp => sp.GetRequiredService<HealthMonitor>());
        return services;
    }
}
