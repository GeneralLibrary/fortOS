using GNAS.Core;
using GNAS.Modules.Host;
using GNAS.Modules.Storage.Services;

namespace GNAS.Modules.Storage;

/// <summary>存储业务模块，封装磁盘、RAID 与文件系统操作。</summary>
public sealed class StorageModule : NasModuleBase
{
    private SmartMonitorService? smartMonitor;

    /// <inheritdoc />
    public override string ModuleId => "storage";

    /// <inheritdoc />
    public override string DisplayName => "存储管理";

    /// <inheritdoc />
    public override IReadOnlyList<string> RequiredCapabilities => ["storage:disk:read", "storage:disk:write", "storage:filesystem:write"];

    /// <summary>列出磁盘。</summary>
    public Task<IReadOnlyList<DiskInfo>> ListDisksAsync(CancellationToken ct) => RequiredService<IDiskManager>().ListDisksAsync(ct);

    /// <summary>获取磁盘详情。</summary>
    public async Task<DiskInfo> GetDiskDetailAsync(string path, CancellationToken ct)
    {
        ValidateDevicePath(path);
        return await RequiredService<IDiskManager>().GetDiskAsync(path, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"磁盘不存在: {path}");
    }

    /// <summary>创建分区。</summary>
    public async Task<PartitionResult> CreatePartitionAsync(string diskPath, PartitionSpec spec, CancellationToken ct)
    {
        ValidateDevicePath(diskPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.Name);
        var result = await RequiredService<IDiskManager>().CreatePartitionAsync(diskPath, spec, ct).ConfigureAwait(false);
        await PublishAsync("storage.partition.created", "storage.partition.created", new { diskPath, spec.Name, result.Success, result.PartitionPath }, ct).ConfigureAwait(false);
        return result;
    }

    /// <summary>创建 RAID 存储池。</summary>
    public async Task<RaidResult> CreateRaidAsync(RaidLevel level, string[] diskPaths, CancellationToken ct)
    {
        if (level == RaidLevel.Unknown)
        {
            throw new ArgumentException("RAID 等级不能为空。", nameof(level));
        }

        if (diskPaths.Length == 0)
        {
            throw new ArgumentException("至少需要一块磁盘。", nameof(diskPaths));
        }

        foreach (var diskPath in diskPaths)
        {
            ValidateDevicePath(diskPath);
        }

        var result = await RequiredService<IDiskManager>().CreateRaidAsync(level, diskPaths, ct).ConfigureAwait(false);
        await PublishAsync("storage.raid.created", "storage.raid.created", new { level, diskPaths, result.Success, result.PoolId }, ct).ConfigureAwait(false);
        return result;
    }

    /// <summary>挂载文件系统。</summary>
    public async Task MountAsync(string device, string mountPoint, string fsType, CancellationToken ct)
    {
        ValidateDevicePath(device);
        ValidateAbsolutePath(mountPoint, nameof(mountPoint));
        ArgumentException.ThrowIfNullOrWhiteSpace(fsType);
        await RequiredService<IFileSystem>().MountAsync(device, mountPoint, fsType, ct).ConfigureAwait(false);
        await PublishAsync("storage.filesystem.mounted", "storage.filesystem.mounted", new { device, mountPoint, fsType }, ct).ConfigureAwait(false);
    }

    /// <summary>卸载文件系统。</summary>
    public async Task UnmountAsync(string mountPoint, CancellationToken ct)
    {
        ValidateAbsolutePath(mountPoint, nameof(mountPoint));
        await RequiredService<IFileSystem>().UnmountAsync(mountPoint, ct).ConfigureAwait(false);
        await PublishAsync("storage.filesystem.unmounted", "storage.filesystem.unmounted", new { mountPoint }, ct).ConfigureAwait(false);
    }

    /// <summary>格式化文件系统。</summary>
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
            throw new ArgumentException("设备路径必须位于 /dev 下。", nameof(path));
        }
    }

    private static void ValidateAbsolutePath(string path, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, paramName);
        if (!Path.IsPathFullyQualified(path) || path.Contains('\n') || path.Contains('\r'))
        {
            throw new ArgumentException("路径必须为不含换行的绝对路径。", paramName);
        }
    }
}
