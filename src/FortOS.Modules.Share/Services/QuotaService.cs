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
        var args = tool == "btrfs" ? $"qgroup limit {bytes} {Quote(path)}" : $"-x -c 'limit -p bhard={bytes} {Quote(path)}' {Quote(path)}";
        try
        {
            return await processManager.ExecuteCommandAsync(new ProcessStartConfig { ExecutablePath = tool, Arguments = args }, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new CommandResult { ExitCode = 127, Stderr = $"Quota tool {tool} is not available, gracefully skipping." };
        }
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}";
}
