namespace GNAS.Core;

/// <summary>服务宿主接口。</summary>
public interface IServiceHost
{
    /// <summary>服务标识。</summary>
    string ServiceId { get; }
    /// <summary>启动服务。</summary>
    Task StartAsync(ServiceDefinition definition, CancellationToken ct);
    /// <summary>停止服务。</summary>
    Task StopAsync(CancellationToken ct);
    /// <summary>获取服务状态。</summary>
    Task<ServiceStatusInfo> GetStatusAsync(CancellationToken ct);
}
