using GNAS.Core;

namespace GNAS.Modules.Share.Services;

/// <summary>配额服务，优先使用 xfs_quota/btrfs qgroup；工具缺失时优雅失败。</summary>
public sealed class QuotaService
{
    private readonly IProcessManager processManager;

    /// <summary>创建配额服务。</summary>
    public QuotaService(IProcessManager processManager)
    {
        this.processManager = processManager;
    }

    /// <summary>设置目录配额。</summary>
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
            return new CommandResult { ExitCode = 127, Stderr = $"配额工具 {tool} 不可用，已优雅跳过。" };
        }
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}";
}
