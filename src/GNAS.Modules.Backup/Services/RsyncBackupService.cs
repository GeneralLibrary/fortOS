using GNAS.Core;

namespace GNAS.Modules.Backup.Services;

/// <summary>Rsync 增量备份服务。</summary>
public sealed class RsyncBackupService
{
    private readonly IProcessManager processManager;

    /// <summary>创建 Rsync 备份服务。</summary>
    public RsyncBackupService(IProcessManager processManager)
    {
        this.processManager = processManager;
    }

    /// <summary>执行增量同步。</summary>
    public async Task<CommandResult> SyncAsync(string source, string target, bool dryRun, CancellationToken ct)
    {
        Validate(source);
        Validate(target);
        var args = $"-a --delete {(dryRun ? "--dry-run " : string.Empty)}{Quote(EnsureTrailingSlash(source))} {Quote(target)}";
        try
        {
            return await processManager.ExecuteCommandAsync(new ProcessStartConfig { ExecutablePath = "rsync", Arguments = args, TimeoutSeconds = 3600 }, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new CommandResult { ExitCode = 127, Stderr = "rsync 不可用，已优雅失败。" };
        }
    }

    private static string EnsureTrailingSlash(string path) => path.EndsWith('/') ? path : path + "/";
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
