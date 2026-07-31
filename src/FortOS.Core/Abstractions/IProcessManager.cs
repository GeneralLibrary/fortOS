namespace FortOS.Core;

/// <summary>Process management abstraction.</summary>
public interface IProcessManager
{
    /// <summary>Start a process.</summary>
    Task<ProcessInfo> StartProcessAsync(ProcessStartConfig config, CancellationToken ct);
    /// <summary>Stop a process.</summary>
    Task StopProcessAsync(int pid, TimeSpan gracefulTimeout, CancellationToken ct);
    /// <summary>Get process information.</summary>
    Task<ProcessInfo?> GetProcessAsync(int pid, CancellationToken ct);
    /// <summary>Execute a command and wait for completion.</summary>
    Task<CommandResult> ExecuteCommandAsync(ProcessStartConfig config, CancellationToken ct);
    /// <summary>Enable a system service.</summary>
    Task EnableServiceAsync(string serviceName, CancellationToken ct);
    /// <summary>Disable a system service.</summary>
    Task DisableServiceAsync(string serviceName, CancellationToken ct);
}
