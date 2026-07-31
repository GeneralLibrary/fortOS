namespace FortOS.Core;

/// <summary>Service supervisor interface.</summary>
public interface IServiceSupervisor
{
    /// <summary>Start a service.</summary>
    Task StartAsync(string serviceId, CancellationToken ct);
    /// <summary>Stop a service.</summary>
    Task StopAsync(string serviceId, CancellationToken ct);
    /// <summary>Restart a service.</summary>
    Task RestartAsync(string serviceId, CancellationToken ct);
    /// <summary>Start all automatic services.</summary>
    Task StartAllAutomaticAsync(CancellationToken ct);
    /// <summary>Shut down all services.</summary>
    Task ShutdownAllAsync(CancellationToken ct);
    /// <summary>Get service status.</summary>
    Task<ServiceStatusInfo> GetStatusAsync(string serviceId, CancellationToken ct);
    /// <summary>List service statuses.</summary>
    Task<IReadOnlyList<ServiceStatusInfo>> ListStatusesAsync(CancellationToken ct);
}
