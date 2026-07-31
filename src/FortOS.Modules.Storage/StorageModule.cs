using FortOS.Core;
using FortOS.Modules.Host;
using FortOS.Modules.Storage.Services;

namespace FortOS.Modules.Storage;

/// <summary>Storage business module, encapsulating disk, RAID, and filesystem operations.</summary>
public sealed class StorageModule : NasModuleBase
{
    private SmartMonitorService? smartMonitor;

    /// <inheritdoc />
    public override string ModuleId => "storage";

    /// <inheritdoc />
    public override string DisplayName => "Storage Management";

    /// <inheritdoc />
    public override IReadOnlyList<string> RequiredCapabilities => ["storage:disk:read", "storage:disk:write", "storage:filesystem:write"];

    /// <summary>List disks.</summary>
    public Task<IReadOnlyList<DiskInfo>> ListDisksAsync(CancellationToken ct) => RequiredService<IDiskManager>().ListDisksAsync(ct);

    /// <summary>Get disk details.</summary>
    public async Task<DiskInfo> GetDiskDetailAsync(string path, CancellationToken ct)
    {
        ValidateDevicePath(path);
        return await RequiredService<IDiskManager>().GetDiskAsync(path, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Disk does not exist: {path}");
    }

    /// <summary>Create partition.</summary>
    public async Task<PartitionResult> CreatePartitionAsync(string diskPath, PartitionSpec spec, CancellationToken ct)
    {
        ValidateDevicePath(diskPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.Name);
        var result = await RequiredService<IDiskManager>().CreatePartitionAsync(diskPath, spec, ct).ConfigureAwait(false);
        await PublishAsync("storage.partition.created", "storage.partition.created", new { diskPath, spec.Name, result.Success, result.PartitionPath }, ct).ConfigureAwait(false);
        return result;
    }

    /// <summary>Create RAID storage pool.</summary>
    public async Task<RaidResult> CreateRaidAsync(RaidLevel level, string[] diskPaths, CancellationToken ct)
    {
        if (level == RaidLevel.Unknown)
        {
            throw new ArgumentException("RAID level cannot be empty.", nameof(level));
        }

        if (diskPaths.Length == 0)
        {
            throw new ArgumentException("At least one disk is required.", nameof(diskPaths));
        }

        foreach (var diskPath in diskPaths)
        {
            ValidateDevicePath(diskPath);
        }

        var result = await RequiredService<IDiskManager>().CreateRaidAsync(level, diskPaths, ct).ConfigureAwait(false);
        await PublishAsync("storage.raid.created", "storage.raid.created", new { level, diskPaths, result.Success, result.PoolId }, ct).ConfigureAwait(false);
        return result;
    }

    /// <summary>Mount filesystem.</summary>
    public async Task MountAsync(string device, string mountPoint, string fsType, CancellationToken ct)
    {
        ValidateDevicePath(device);
        ValidateAbsolutePath(mountPoint, nameof(mountPoint));
        ArgumentException.ThrowIfNullOrWhiteSpace(fsType);
        await RequiredService<IFileSystem>().MountAsync(device, mountPoint, fsType, ct).ConfigureAwait(false);
        await PublishAsync("storage.filesystem.mounted", "storage.filesystem.mounted", new { device, mountPoint, fsType }, ct).ConfigureAwait(false);
    }

    /// <summary>Unmount filesystem.</summary>
    public async Task UnmountAsync(string mountPoint, CancellationToken ct)
    {
        ValidateAbsolutePath(mountPoint, nameof(mountPoint));
        await RequiredService<IFileSystem>().UnmountAsync(mountPoint, ct).ConfigureAwait(false);
        await PublishAsync("storage.filesystem.unmounted", "storage.filesystem.unmounted", new { mountPoint }, ct).ConfigureAwait(false);
    }

    /// <summary>Format filesystem.</summary>
    public async Task FormatAsync(string device, string fsType, CancellationToken ct)
    {
        ValidateDevicePath(device);
        ArgumentException.ThrowIfNullOrWhiteSpace(fsType);
        await RequiredService<IFileSystem>().FormatAsync(device, fsType, ct).ConfigureAwait(false);
        await PublishAsync("storage.filesystem.formatted", "storage.filesystem.formatted", new { device, fsType }, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override Task OnInitializeAsync(CancellationToken ct)
    {
        var diskManager = Services.GetService(typeof(IDiskManager)) as IDiskManager;
        if (diskManager is not null)
        {
            smartMonitor = new SmartMonitorService(diskManager, EventBus, Services, Logger);
            smartMonitor.Start(ct);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override async Task OnShutdownAsync(CancellationToken ct)
    {
        if (smartMonitor is not null)
        {
            await smartMonitor.StopAsync(ct).ConfigureAwait(false);
        }
    }

    private static void ValidateDevicePath(string path)
    {
        ValidateAbsolutePath(path, nameof(path));
        if (!path.StartsWith("/dev/", StringComparison.Ordinal))
        {
            throw new ArgumentException("Device path must be under /dev.", nameof(path));
        }
    }

    private static void ValidateAbsolutePath(string path, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, paramName);
        if (!Path.IsPathFullyQualified(path) || path.Contains('\n') || path.Contains('\r'))
        {
            throw new ArgumentException("Path must be an absolute path without newlines.", paramName);
        }
    }
}
