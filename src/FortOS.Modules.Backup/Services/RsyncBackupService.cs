using System.Text;
using FortOS.Core;

namespace FortOS.Modules.Backup.Services;

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
    public async Task<CommandResult> SyncAsync(string source, string target, bool dryRun, CancellationToken ct, string[]? excludePatterns = null)
    {
        Validate(source);
        Validate(target);

        // Data protection: refuse to sync when the source directory is missing or empty. Combined with unconditional --delete, an empty source would
        // wipe the entire target directory. An empty source usually means a mount failure or wrong path; better to fail than to empty the target.
        if (!Directory.Exists(source))
        {
            return new CommandResult { ExitCode = 3, Stderr = $"Source directory does not exist; refusing to sync: {source}" };
        }

        if (!new DirectoryInfo(source).EnumerateFileSystemInfos().Any())
        {
            return new CommandResult { ExitCode = 3, Stderr = $"Source directory is empty; refusing to sync with --delete: {source}" };
        }

        var args = new StringBuilder("-a --delete ");
        if (dryRun) args.Append("--dry-run ");
        foreach (var pattern in excludePatterns ?? [])
        {
            // One --exclude argument per pattern; patterns are configured by the administrator and passed through verbatim as rsync
            // filter patterns (--exclude=value is not re-parsed by rsync as an option).
            args.Append("--exclude=").Append(Quote(pattern)).Append(' ');
        }
        args.Append(Quote(EnsureTrailingSlash(source))).Append(' ').Append(Quote(target));
        try
        {
            return await processManager.ExecuteCommandAsync(new ProcessStartConfig { ExecutablePath = "rsync", Arguments = args.ToString(), TimeoutSeconds = 3600 }, ct).ConfigureAwait(false);
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

    // rsync receives the path as a single shell-style quoted token inside a pre-built argument
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
