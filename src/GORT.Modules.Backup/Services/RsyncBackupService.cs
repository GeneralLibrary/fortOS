using GORT.Core;

namespace GORT.Modules.Backup.Services;

/// <summary>Rsync incremental backup service.</summary>
public sealed class RsyncBackupService
{
    private readonly IProcessManager processManager;

    /// <summary>Creates an Rsync backup service.</summary>
    public RsyncBackupService(IProcessManager processManager)
    {
        this.processManager = processManager;
    }

    /// <summary>Performs incremental sync.</summary>
    public async Task<CommandResult> SyncAsync(string source, string target, bool dryRun, CancellationToken ct)
    {
        Validate(source);
        Validate(target);
        var args = $"-a --delete {(dryRun ? "--dry-run " : string.Empty)}{Quote(EnsureTrailingSlash(source))} {Quote(target)}";
        try
        {
            return await processManager.ExecuteCommandAsync(new ProcessStartConfig { ExecutablePath = "rsync", Arguments = args, TimeoutSeconds = 3600 }, ct).ConfigureAwait(false);
        }
        catch (CommandExecutionException ex)
        {
            return new CommandResult { ExitCode = ex.ExitCode, Stdout = ex.Stdout, Stderr = ex.Stderr };
        }
        catch (Exception ex) when (ex is FileNotFoundException or System.ComponentModel.Win32Exception or InvalidOperationException or PlatformException)
        {
            return new CommandResult { ExitCode = 127, Stderr = "rsync is not available, gracefully failed." };
        }
    }

    private static string EnsureTrailingSlash(string path) => path.EndsWith('/') ? path : path + "/";
    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}";
    private static void Validate(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Contains('\n') || value.Contains('\r'))
        {
            throw new ArgumentException("Path cannot contain newlines.", nameof(value));
        }
    }
}
