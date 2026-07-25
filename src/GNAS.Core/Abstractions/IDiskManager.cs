namespace GNAS.Core;

/// <summary>磁盘管理抽象。</summary>
public interface IDiskManager
{
    /// <summary>列出磁盘。</summary>
    Task<IReadOnlyList<DiskInfo>> ListDisksAsync(CancellationToken ct);
    /// <summary>获取磁盘。</summary>
    Task<DiskInfo?> GetDiskAsync(string path, CancellationToken ct);
    /// <summary>创建分区。</summary>
    Task<PartitionResult> CreatePartitionAsync(string diskPath, PartitionSpec spec, CancellationToken ct);
    /// <summary>创建 RAID。</summary>
    Task<RaidResult> CreateRaidAsync(RaidLevel level, string[] diskPaths, CancellationToken ct);
    /// <summary>读取 SMART 数据。</summary>
    Task<SmartData> GetSmartDataAsync(string diskPath, CancellationToken ct);
    /// <summary>擦除磁盘。</summary>
    Task WipeDiskAsync(string diskPath, CancellationToken ct);
}
