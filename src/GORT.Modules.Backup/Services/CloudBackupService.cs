using GORT.Core;

namespace GORT.Modules.Backup.Services;

/// <summary>rclone cloud backup service, fails gracefully when rclone is missing.</summary>
public sealed class CloudBackupService
{
    private readonly IProcessManager processManager;

    /// <summary>Creates a cloud backup service.</summary>
    public CloudBackupService(IProcessManager processManager)
    {
        this.processManager = processManager;
    }

    /// <summary>Checks rclone configuration.</summary>
    public Task<CommandResult> CheckConfigAsync(CancellationToken ct) => ExecuteAsync("config file", ct);

    /// <summary>Syncs to remote.</summary>
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
            return new CommandResult { ExitCode = 127, Stderr = "rclone is not available, gracefully failed." };
        }
    }

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
