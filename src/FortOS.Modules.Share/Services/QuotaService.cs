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
        // 注意：CommandExecutor 无 shell，参数经 ProcessStartInfo.Arguments 解析，
        // 单引号不会被移除而是作为字面字符传给工具；含空格的 -c 子命令必须用双引号分组。
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

    /// <summary>完整双引号包裹：先转义内部引号，再闭合尾引号（历史实现只开不闭，命令必然失败）。</summary>
    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
