using GORT.Core;

namespace GORT.Modules.Backup.Services;

/// <summary>Filesystem snapshot service, supports btrfs and zfs, other filesystems gracefully return not supported.</summary>
public sealed class SnapshotService
{
    private readonly IProcessManager processManager;
    private readonly IFileSystem fileSystem;

    /// <summary>Creates a snapshot service.</summary>
    public SnapshotService(IProcessManager processManager, IFileSystem fileSystem)
    {
        this.processManager = processManager;
        this.fileSystem = fileSystem;
    }

    /// <summary>Creates a snapshot.</summary>
    public async Task<CommandResult> CreateSnapshotAsync(string target, string snapshotName, CancellationToken ct)
    {
        ValidatePath(target);
        ValidateName(snapshotName);
        var fs = await fileSystem.GetFilesystemInfoAsync(target, ct).ConfigureAwait(false);
        return fs.FileSystemType.ToLowerInvariant() switch
        {
            "btrfs" => await processManager.ExecuteCommandAsync(new ProcessStartConfig { ExecutablePath = "btrfs", Arguments = $"subvolume snapshot -r {Quote(target)} {Quote(Path.Combine(target, ".snapshots", snapshotName))}" }, ct).ConfigureAwait(false),
            "zfs" => await processManager.ExecuteCommandAsync(new ProcessStartConfig { ExecutablePath = "zfs", Arguments = $"snapshot {target}@{snapshotName}" }, ct).ConfigureAwait(false),
            _ => new CommandResult { ExitCode = 95, Stderr = $"Filesystem {fs.FileSystemType} does not support native snapshots." }
        };
    }

    /// <summary>Lists snapshots.</summary>
    public async Task<CommandResult> ListSnapshotsAsync(string target, CancellationToken ct)
    {
        ValidatePath(target);
        var fs = await fileSystem.GetFilesystemInfoAsync(target, ct).ConfigureAwait(false);
        return fs.FileSystemType.ToLowerInvariant() switch
        {
            "btrfs" => await processManager.ExecuteCommandAsync(new ProcessStartConfig { ExecutablePath = "btrfs", Arguments = $"subvolume list -s {Quote(target)}" }, ct).ConfigureAwait(false),
            "zfs" => await processManager.ExecuteCommandAsync(new ProcessStartConfig { ExecutablePath = "zfs", Arguments = $"list -t snapshot -o name -H {target}" }, ct).ConfigureAwait(false),
            _ => new CommandResult { ExitCode = 95, Stderr = $"Filesystem {fs.FileSystemType} does not support native snapshots." }
        };
    }

    /// <summary>Restores a snapshot.</summary>
    public Task<CommandResult> RestoreSnapshotAsync(string snapshot, string target, CancellationToken ct)
    {
        ValidateName(snapshot);
        ValidatePath(target);
        return processManager.ExecuteCommandAsync(new ProcessStartConfig { ExecutablePath = "btrfs", Arguments = $"subvolume snapshot {Quote(snapshot)} {Quote(target)}" }, ct);
    }

    /// <summary>Deletes a snapshot.</summary>
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
            throw new ArgumentException("Path cannot contain newlines.", nameof(path));
        }
    }

    private static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Contains('\n') || name.Contains('\r') || name.Contains(';') || name.Contains(' '))
        {
            throw new ArgumentException("Invalid snapshot name.", nameof(name));
        }
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
