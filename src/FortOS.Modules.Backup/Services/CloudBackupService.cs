using FortOS.Core;

namespace FortOS.Modules.Backup.Services;

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

    // rclone receives the path as a single shell-style quoted token inside a pre-built argument
    // string, so quotes must both open and close (and embedded quotes be escaped) or a path
    // containing spaces would be split into multiple arguments.
    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
    private static void Validate(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Contains('\n') || value.Contains('\r'))
        {
            throw new ArgumentException("Path cannot contain newlines.", nameof(value));
        }
    }
}
