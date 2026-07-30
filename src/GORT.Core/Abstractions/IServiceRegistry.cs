namespace GORT.Core;

/// <summary>Service registry interface.</summary>
public interface IServiceRegistry
{
    /// <summary>Register a service definition.</summary>
    Task RegisterAsync(ServiceDefinition definition, CancellationToken ct);
    /// <summary>Get a service definition.</summary>
    Task<ServiceDefinition?> GetAsync(string serviceId, CancellationToken ct);
    /// <summary>List service definitions.</summary>
    Task<IReadOnlyList<ServiceDefinition>> ListAsync(CancellationToken ct);
    /// <summary>Update a service definition.</summary>
    Task UpdateAsync(ServiceDefinition definition, CancellationToken ct);
    /// <summary>Unregister a service definition.</summary>
    Task UnregisterAsync(string serviceId, CancellationToken ct);
    /// <summary>Get services that depend on the specified service.</summary>
    Task<IReadOnlyList<ServiceDefinition>> GetDependentsAsync(string serviceId, CancellationToken ct);
    /// <summary>Get services that the specified service depends on.</summary>
    Task<IReadOnlyList<ServiceDefinition>> GetDependenciesAsync(string serviceId, CancellationToken ct);
}
