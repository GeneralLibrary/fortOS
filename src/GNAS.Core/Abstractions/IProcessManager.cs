namespace GNAS.Core;

/// <summary>进程管理抽象。</summary>
public interface IProcessManager
{
    /// <summary>启动进程。</summary>
    Task<ProcessInfo> StartProcessAsync(ProcessStartConfig config, CancellationToken ct);
    /// <summary>停止进程。</summary>
    Task StopProcessAsync(int pid, TimeSpan gracefulTimeout, CancellationToken ct);
    /// <summary>获取进程信息。</summary>
    Task<ProcessInfo?> GetProcessAsync(int pid, CancellationToken ct);
    /// <summary>执行命令并等待完成。</summary>
    Task<CommandResult> ExecuteCommandAsync(ProcessStartConfig config, CancellationToken ct);
    /// <summary>启用系统服务。</summary>
    Task EnableServiceAsync(string serviceName, CancellationToken ct);
    /// <summary>禁用系统服务。</summary>
    Task DisableServiceAsync(string serviceName, CancellationToken ct);
}
