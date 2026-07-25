using GNAS.Core;
using Microsoft.Extensions.DependencyInjection;

namespace GNAS.ServiceBus.Hosts;

/// <summary>
/// 服务宿主工厂。
/// </summary>
public static class ServiceHostFactory
{
    /// <summary>
    /// 按服务类型创建宿主。
    /// </summary>
    /// <param name="definition">服务定义。</param>
    /// <param name="serviceProvider">服务提供器。</param>
    /// <returns>服务宿主。</returns>
    public static IServiceHost Create(ServiceDefinition definition, IServiceProvider serviceProvider)
        => definition.Type switch
        {
            ServiceType.Native => ActivatorUtilities.CreateInstance<NativeServiceHost>(serviceProvider),
            ServiceType.Systemd => ActivatorUtilities.CreateInstance<SystemdServiceHost>(serviceProvider),
            ServiceType.Container => ActivatorUtilities.CreateInstance<ContainerServiceHost>(serviceProvider),
            ServiceType.Module => throw new ArgumentException("模块服务由 ModuleHost 进程内托管。", nameof(definition)),
            _ => throw new ArgumentOutOfRangeException(nameof(definition), definition.Type, "未知服务类型。"),
        };
}
