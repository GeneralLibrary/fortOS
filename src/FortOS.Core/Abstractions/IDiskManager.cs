namespace FortOS.Core;

/// <summary>Disk management abstraction.</summary>
public interface IDiskManager
{
    /// <summary>List disks.</summary>
    Task<IReadOnlyList<DiskInfo>> ListDisksAsync(CancellationToken ct);
    /// <summary>Get disk info.</summary>
    Task<DiskInfo?> GetDiskAsync(string path, CancellationToken ct);
    /// <summary>Create a partition.</summary>
    Task<PartitionResult> CreatePartitionAsync(string diskPath, PartitionSpec spec, CancellationToken ct);
    /// <summary>Create a RAID array.</summary>
    Task<RaidResult> CreateRaidAsync(RaidLevel level, string[] diskPaths, CancellationToken ct);
    /// <summary>List active MD RAID arrays.</summary>
    Task<IReadOnlyList<RaidMetrics>> ListRaidsAsync(CancellationToken ct);
    /// <summary>Read block-device status (filesystem, mount point) for an arbitrary device.</summary>
    Task<DeviceStatus> GetDeviceStatusAsync(string path, CancellationToken ct);
    /// <summary>Read SMART data.</summary>
    Task<SmartData> GetSmartDataAsync(string diskPath, CancellationToken ct);
    /// <summary>Wipe a disk.</summary>
    Task WipeDiskAsync(string diskPath, CancellationToken ct);
}
