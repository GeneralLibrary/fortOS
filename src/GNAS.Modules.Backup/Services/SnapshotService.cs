using GNAS.Core;

namespace GNAS.Modules.Backup.Services;

/// <summary>文件系统快照服务，支持 btrfs 与 zfs，其他文件系统优雅返回不支持。</summary>
public sealed class SnapshotService
{
    private readonly IProcessManager processManager;
    private readonly IFileSystem fileSystem;

    /// <summary>创建快照服务。</summary>
    public SnapshotService(IProcessManager processManager, IFileSystem fileSystem)
    {
        this.processManager = processManager;
        this.fileSystem = fileSystem;
    }

    /// <summary>创建快照。</summary>
    public async Task<CommandResult> CreateSnapshotAsync(string target, string snapshotName, CancellationToken ct)
    {
        ValidatePath(target);
        ValidateName(snapshotName);
        var fs = await fileSystem.GetFilesystemInfoAsync(target, ct).ConfigureAwait(false);
        return fs.FileSystemType.ToLowerInvariant() switch
        {
            "btrfs" => await processManager.ExecuteCommandAsync(new ProcessStartConfig { ExecutablePath = "btrfs", Arguments = $"subvolume snapshot -r {Quote(target)} {Quote(Path.Combine(target, ".snapshots", snapshotName))}" }, ct).ConfigureAwait(false),
            "zfs" => await processManager.ExecuteCommandAsync(new ProcessStartConfig { ExecutablePath = "zfs", Arguments = $"snapshot {target}@{snapshotName}" }, ct).ConfigureAwait(false),
            _ => new CommandResult { ExitCode = 95, Stderr = $"文件系统 {fs.FileSystemType} 不支持原生快照。" }
        };
    }

    /// <summary>列出快照。</summary>
    public async Task<CommandResult> ListSnapshotsAsync(string target, CancellationToken ct)
    {
        ValidatePath(target);
        var fs = await fileSystem.GetFilesystemInfoAsync(target, ct).ConfigureAwait(false);
        return fs.FileSystemType.ToLowerInvariant() switch
        {
            "btrfs" => await processManager.ExecuteCommandAsync(new ProcessStartConfig { ExecutablePath = "btrfs", Arguments = $"subvolume list -s {Quote(target)}" }, ct).ConfigureAwait(false),
            "zfs" => await processManager.ExecuteCommandAsync(new ProcessStartConfig { ExecutablePath = "zfs", Arguments = $"list -t snapshot -o name -H {target}" }, ct).ConfigureAwait(false),
            _ => new CommandResult { ExitCode = 95, Stderr = $"文件系统 {fs.FileSystemType} 不支持原生快照。" }
        };
    }

    /// <summary>恢复快照。</summary>
    public Task<CommandResult> RestoreSnapshotAsync(string snapshot, string target, CancellationToken ct)
    {
        ValidateName(snapshot);
        ValidatePath(target);
        return processManager.ExecuteCommandAsync(new ProcessStartConfig { ExecutablePath = "btrfs", Arguments = $"subvolume snapshot {Quote(snapshot)} {Quote(target)}" }, ct);
    }

    /// <summary>删除快照。</summary>
    public Task<CommandResult> DeleteSnapshotAsync(string snapshot, CancellationToken ct)
    {
        ValidatePath(snapshot);
        return processManager.ExecuteCommandAsync(new ProcessStartConfig { ExecutablePath = "btrfs", Arguments = $"subvolume delete {Quote(snapshot)}" }, ct);
    }

    private static void ValidatePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.Contains('\n') || path.Contains('\r'))
        {
            throw new ArgumentException("路径不能包含换行。", nameof(path));
        }
    }

    private static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Contains('\n') || name.Contains('\r') || name.Contains(';') || name.Contains(' '))
        {
            throw new ArgumentException("快照名称非法。", nameof(name));
        }
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
