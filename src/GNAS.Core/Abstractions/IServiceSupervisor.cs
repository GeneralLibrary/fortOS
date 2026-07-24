namespace GNAS.Core;

/// <summary>服务监管器接口。</summary>
public interface IServiceSupervisor
{
    /// <summary>启动服务。</summary>
    Task StartAsync(string serviceId, CancellationToken ct);
    /// <summary>停止服务。</summary>
    Task StopAsync(string serviceId, CancellationToken ct);
    /// <summary>重启服务。</summary>
    Task RestartAsync(string serviceId, CancellationToken ct);
    /// <summary>启动所有自动服务。</summary>
    Task StartAllAutomaticAsync(CancellationToken ct);
    /// <summary>关闭所有服务。</summary>
    Task ShutdownAllAsync(CancellationToken ct);
    /// <summary>获取服务状态。</summary>
    Task<ServiceStatusInfo> GetStatusAsync(string serviceId, CancellationToken ct);
    /// <summary>列出服务状态。</summary>
    Task<IReadOnlyList<ServiceStatusInfo>> ListStatusesAsync(CancellationToken ct);
}
