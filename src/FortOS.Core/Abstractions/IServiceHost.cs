namespace FortOS.Core;

/// <summary>Service host interface.</summary>
public interface IServiceHost
{
    /// <summary>Service ID.</summary>
    string ServiceId { get; }
    /// <summary>Start the service.</summary>
    Task StartAsync(ServiceDefinition definition, CancellationToken ct);
    /// <summary>Stop the service.</summary>
    Task StopAsync(CancellationToken ct);
    /// <summary>Get service status.</summary>
    Task<ServiceStatusInfo> GetStatusAsync(CancellationToken ct);
}
