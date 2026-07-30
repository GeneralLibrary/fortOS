using GORT.Core;
using Microsoft.Extensions.DependencyInjection;

namespace GORT.ServiceBus.Hosts;

/// <summary>
/// Service host factory.
/// </summary>
public static class ServiceHostFactory
{
    /// <summary>
    /// Creates a host by service type.
    /// </summary>
    /// <param name="definition">Service definition.</param>
    /// <param name="serviceProvider">Service provider.</param>
    /// <returns>Service host.</returns>
    public static IServiceHost Create(ServiceDefinition definition, IServiceProvider serviceProvider)
        => definition.Type switch
        {
            ServiceType.Native => ActivatorUtilities.CreateInstance<NativeServiceHost>(serviceProvider),
            ServiceType.Systemd => ActivatorUtilities.CreateInstance<SystemdServiceHost>(serviceProvider),
            ServiceType.Container => ActivatorUtilities.CreateInstance<ContainerServiceHost>(serviceProvider),
            ServiceType.Module => throw new ArgumentException("Module services are hosted in-process by ModuleHost.", nameof(definition)),
            _ => throw new ArgumentOutOfRangeException(nameof(definition), definition.Type, "Unknown service type."),
        };
}
