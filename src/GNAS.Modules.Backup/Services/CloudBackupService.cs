using GNAS.Core;

namespace GNAS.Modules.Backup.Services;

/// <summary>rclone 云备份服务，rclone 缺失时优雅失败。</summary>
public sealed class CloudBackupService
{
    private readonly IProcessManager processManager;

    /// <summary>创建云备份服务。</summary>
    public CloudBackupService(IProcessManager processManager)
    {
        this.processManager = processManager;
    }

    /// <summary>检查 rclone 配置。</summary>
    public Task<CommandResult> CheckConfigAsync(CancellationToken ct) => ExecuteAsync("config file", ct);

    /// <summary>同步到远程。</summary>
    public Task<CommandResult> SyncAsync(string source, string remote, CancellationToken ct)
    {
        Validate(source);
        Validate(remote);
        return ExecuteAsync($"sync {Quote(source)} {Quote(remote)}", ct);
    }

    private async Task<CommandResult> ExecuteAsync(string arguments, CancellationToken ct)
    {
        try
        {
            return await processManager.ExecuteCommandAsync(new ProcessStartConfig { ExecutablePath = "rclone", Arguments = arguments, TimeoutSeconds = 7200 }, ct).ConfigureAwait(false);
        }
        catch (CommandExecutionException ex)
        {
            return new CommandResult { ExitCode = ex.ExitCode, Stdout = ex.Stdout, Stderr = ex.Stderr };
        }
        catch (Exception ex) when (ex is FileNotFoundException or System.ComponentModel.Win32Exception or InvalidOperationException or PlatformException)
        {
            return new CommandResult { ExitCode = 127, Stderr = "rclone 不可用，已优雅失败。" };
        }
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}";
    private static void Validate(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Contains('\n') || value.Contains('\r'))
        {
            throw new ArgumentException("路径不能包含换行。", nameof(value));
        }
    }
}
