using GNAS.Core;
using GNAS.ServiceBus.Events;
using GNAS.ServiceBus.Health;
using GNAS.ServiceBus.Registry;
using GNAS.ServiceBus.Supervisor;
using Microsoft.Extensions.DependencyInjection;

namespace GNAS.ServiceBus;

/// <summary>
/// 服务总线依赖注入扩展。
/// </summary>
public static class ServiceBusExtensions
{
    /// <summary>
    /// 注册服务总线组件。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合。</returns>
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
