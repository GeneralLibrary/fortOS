using FortOS.Core;

namespace FortOS.Modules.Share.Services;

/// <summary>Quota service, prefers xfs_quota/btrfs qgroup; fails gracefully when tools are missing.</summary>
public sealed class QuotaService
{
    private readonly IProcessManager processManager;

    /// <summary>Create quota service.</summary>
    public QuotaService(IProcessManager processManager)
    {
        this.processManager = processManager;
    }

    /// <summary>Set directory quota.</summary>
    public async Task<CommandResult> SetQuotaAsync(string path, long bytes, string fileSystemType, CancellationToken ct)
    {
        ShareValidation.ValidatePath(path);
        var tool = fileSystemType.Equals("btrfs", StringComparison.OrdinalIgnoreCase) ? "btrfs" : "xfs_quota";
        // Note: CommandExecutor has no shell; arguments are parsed via ProcessStartInfo.Arguments, so single quotes are not stripped but
        // passed to the tool as literal characters; a -c subcommand containing spaces must be grouped with double quotes.
        var args = tool == "btrfs"
            ? $"qgroup limit {bytes} {Quote(path)}"
            : $"-x -c {Quote($"limit -p bhard={bytes} {path}")} {Quote(path)}";
        try
        {
            return await processManager.ExecuteCommandAsync(new ProcessStartConfig { ExecutablePath = tool, Arguments = args }, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new CommandResult { ExitCode = 127, Stderr = $"Quota tool {tool} is not available, gracefully skipping." };
        }
    }

    /// <summary>Wraps in complete double quotes: escapes inner quotes first, then closes the trailing quote (the historical implementation only opened the quote, so the command always failed).</summary>
    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
