namespace GNAS.Core;

/// <summary>服务注册表接口。</summary>
public interface IServiceRegistry
{
    /// <summary>注册服务定义。</summary>
    Task RegisterAsync(ServiceDefinition definition, CancellationToken ct);
    /// <summary>获取服务定义。</summary>
    Task<ServiceDefinition?> GetAsync(string serviceId, CancellationToken ct);
    /// <summary>列出服务定义。</summary>
    Task<IReadOnlyList<ServiceDefinition>> ListAsync(CancellationToken ct);
    /// <summary>更新服务定义。</summary>
    Task UpdateAsync(ServiceDefinition definition, CancellationToken ct);
    /// <summary>注销服务定义。</summary>
    Task UnregisterAsync(string serviceId, CancellationToken ct);
    /// <summary>获取依赖当前服务的服务。</summary>
    Task<IReadOnlyList<ServiceDefinition>> GetDependentsAsync(string serviceId, CancellationToken ct);
    /// <summary>获取当前服务依赖的服务。</summary>
    Task<IReadOnlyList<ServiceDefinition>> GetDependenciesAsync(string serviceId, CancellationToken ct);
}
