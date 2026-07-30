namespace GORT.Core;

/// <summary>
/// Point-in-time view of the host and the workloads managed by GORT.
/// Rates are normalized to seconds so callers do not need to know the collection interval.
/// </summary>
public sealed record SystemMetricsSnapshot
{
    /// <summary>UTC time at which collection completed.</summary>
    public DateTimeOffset CollectedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Host uptime and load information.</summary>
    public required HostRuntimeMetrics Host { get; init; }

    /// <summary>CPU utilization information.</summary>
    public required CpuMetrics Cpu { get; init; }

    /// <summary>Physical memory and swap utilization.</summary>
    public required MemoryMetrics Memory { get; init; }

    /// <summary>Per-block-device I/O rates.</summary>
    public IReadOnlyList<DiskIoMetrics> Disks { get; init; } = [];

    /// <summary>Per-network-interface traffic rates and error counters.</summary>
    public IReadOnlyList<NetworkTrafficMetrics> Networks { get; init; } = [];

    /// <summary>Host TCP stack health.</summary>
    public NetworkStackMetrics NetworkStack { get; init; } = new();

    /// <summary>Current established sessions for common NAS protocols.</summary>
    public IReadOnlyList<ProtocolSessionMetrics> ProtocolSessions { get; init; } = [];

    /// <summary>Mounted filesystem capacity and growth estimates.</summary>
    public IReadOnlyList<FileSystemCapacityMetrics> FileSystems { get; init; } = [];

    /// <summary>Linux software RAID state.</summary>
    public IReadOnlyList<RaidMetrics> RaidArrays { get; init; } = [];

    /// <summary>Running systemd service state.</summary>
    public IReadOnlyList<ServiceRuntimeMetrics> Services { get; init; } = [];

    /// <summary>Docker container resource consumption.</summary>
    public IReadOnlyList<ContainerRuntimeMetrics> Containers { get; init; } = [];

    /// <summary>
    /// Non-fatal subsystem collection failures. A partial snapshot remains usable when, for
    /// example, Docker is not installed or SMART data is unavailable.
    /// </summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
}

/// <summary>Host runtime metrics.</summary>
public sealed record HostRuntimeMetrics
{
    /// <summary>Host uptime.</summary>
    public TimeSpan Uptime { get; init; }

    /// <summary>System boot time derived from uptime.</summary>
    public DateTimeOffset BootedAt { get; init; }

    /// <summary>One-minute load average.</summary>
    public double LoadAverage1 { get; init; }

    /// <summary>Five-minute load average.</summary>
    public double LoadAverage5 { get; init; }

    /// <summary>Fifteen-minute load average.</summary>
    public double LoadAverage15 { get; init; }
}

/// <summary>CPU utilization metrics.</summary>
public sealed record CpuMetrics
{
    /// <summary>Logical processor count.</summary>
    public int LogicalProcessorCount { get; init; }

    /// <summary>Total non-idle CPU utilization percentage.</summary>
    public double UsagePercent { get; init; }

    /// <summary>User-space CPU utilization percentage.</summary>
    public double UserPercent { get; init; }

    /// <summary>Kernel CPU utilization percentage.</summary>
    public double SystemPercent { get; init; }

    /// <summary>I/O wait percentage.</summary>
    public double IoWaitPercent { get; init; }
}

/// <summary>Memory and swap metrics.</summary>
public sealed record MemoryMetrics
{
    /// <summary>Total physical memory in bytes.</summary>
    public long TotalBytes { get; init; }

    /// <summary>Currently available physical memory in bytes.</summary>
    public long AvailableBytes { get; init; }

    /// <summary>Physical memory in active use in bytes.</summary>
    public long UsedBytes { get; init; }

    /// <summary>Physical memory utilization percentage.</summary>
    public double UsedPercent { get; init; }

    /// <summary>Total swap in bytes.</summary>
    public long SwapTotalBytes { get; init; }

    /// <summary>Swap currently in use in bytes.</summary>
    public long SwapUsedBytes { get; init; }

    /// <summary>Swap utilization percentage.</summary>
    public double SwapUsedPercent { get; init; }

    /// <summary>OOM kills observed since the previous collection.</summary>
    public long OomKillsSinceLastCollection { get; init; }
}

/// <summary>Per-device disk I/O rates and health.</summary>
public sealed record DiskIoMetrics
{
    /// <summary>Kernel block-device name.</summary>
    public required string Device { get; init; }

    /// <summary>Bytes read per second.</summary>
    public double ReadBytesPerSecond { get; init; }

    /// <summary>Bytes written per second.</summary>
    public double WriteBytesPerSecond { get; init; }

    /// <summary>Read operations per second.</summary>
    public double ReadOperationsPerSecond { get; init; }

    /// <summary>Write operations per second.</summary>
    public double WriteOperationsPerSecond { get; init; }

    /// <summary>Average completed I/O latency in milliseconds.</summary>
    public double AverageLatencyMilliseconds { get; init; }

    /// <summary>Percentage of the sample interval during which the device was busy.</summary>
    public double UtilizationPercent { get; init; }

    /// <summary>SMART temperature when available.</summary>
    public int? TemperatureCelsius { get; init; }

    /// <summary>SMART health state when available.</summary>
    public string? SmartHealth { get; init; }
}

/// <summary>Per-interface network traffic rates.</summary>
public sealed record NetworkTrafficMetrics
{
    /// <summary>Network interface name.</summary>
    public required string Interface { get; init; }

    /// <summary>Whether the interface is operationally up.</summary>
    public bool IsUp { get; init; }

    /// <summary>Link speed in megabits per second when known.</summary>
    public long? LinkSpeedMbps { get; init; }

    /// <summary>Received bytes per second.</summary>
    public double ReceiveBytesPerSecond { get; init; }

    /// <summary>Transmitted bytes per second.</summary>
    public double TransmitBytesPerSecond { get; init; }

    /// <summary>Cumulative receive errors reported by the kernel.</summary>
    public long ReceiveErrors { get; init; }

    /// <summary>Cumulative transmit errors reported by the kernel.</summary>
    public long TransmitErrors { get; init; }

    /// <summary>Cumulative receive drops reported by the kernel.</summary>
    public long ReceiveDropped { get; init; }

    /// <summary>Cumulative transmit drops reported by the kernel.</summary>
    public long TransmitDropped { get; init; }
}

/// <summary>Host TCP stack health metrics.</summary>
public sealed record NetworkStackMetrics
{
    /// <summary>Currently established TCP connections.</summary>
    public long EstablishedConnections { get; init; }

    /// <summary>TCP retransmitted segments per second.</summary>
    public double RetransmittedSegmentsPerSecond { get; init; }
}

/// <summary>Established client sessions for a NAS protocol.</summary>
public sealed record ProtocolSessionMetrics
{
    /// <summary>Protocol name, such as smb, nfs, ftp, or ssh.</summary>
    public required string Protocol { get; init; }

    /// <summary>Established TCP session count.</summary>
    public int ActiveSessions { get; init; }
}

/// <summary>Mounted filesystem capacity and projected exhaustion.</summary>
public sealed record FileSystemCapacityMetrics
{
    /// <summary>Filesystem device or source.</summary>
    public required string Device { get; init; }

    /// <summary>Mount point.</summary>
    public required string MountPoint { get; init; }

    /// <summary>Filesystem type when known.</summary>
    public string? FileSystemType { get; init; }

    /// <summary>Total capacity in bytes.</summary>
    public long TotalBytes { get; init; }

    /// <summary>Used capacity in bytes.</summary>
    public long UsedBytes { get; init; }

    /// <summary>Available capacity in bytes.</summary>
    public long AvailableBytes { get; init; }

    /// <summary>Capacity utilization percentage.</summary>
    public double UsedPercent { get; init; }

    /// <summary>Observed byte growth per second. Negative values indicate reclamation.</summary>
    public double GrowthBytesPerSecond { get; init; }

    /// <summary>Estimated exhaustion time; null until positive growth has been observed.</summary>
    public DateTimeOffset? EstimatedFullAt { get; init; }
}

/// <summary>Linux MD RAID health and rebuild state.</summary>
public sealed record RaidMetrics
{
    /// <summary>RAID device name, such as md0.</summary>
    public required string Name { get; init; }

    /// <summary>RAID level reported by the kernel.</summary>
    public required string Level { get; init; }

    /// <summary>Whether all expected members are online.</summary>
    public bool Healthy { get; init; }

    /// <summary>Number of online members.</summary>
    public int ActiveDevices { get; init; }

    /// <summary>Expected member count.</summary>
    public int TotalDevices { get; init; }

    /// <summary>Recovery, reshape, resync, or check operation name.</summary>
    public string? Operation { get; init; }

    /// <summary>Current operation completion percentage.</summary>
    public double? ProgressPercent { get; init; }
}

/// <summary>systemd service runtime information.</summary>
public sealed record ServiceRuntimeMetrics
{
    /// <summary>systemd unit name.</summary>
    public required string ServiceId { get; init; }

    /// <summary>Active state reported by systemd.</summary>
    public required string State { get; init; }

    /// <summary>Continuous running duration.</summary>
    public TimeSpan Uptime { get; init; }

    /// <summary>Restart counter reported by systemd.</summary>
    public long RestartCount { get; init; }
}

/// <summary>Docker container runtime resource information.</summary>
public sealed record ContainerRuntimeMetrics
{
    /// <summary>Container identifier.</summary>
    public required string ContainerId { get; init; }

    /// <summary>Container name.</summary>
    public required string Name { get; init; }

    /// <summary>CPU utilization percentage.</summary>
    public double CpuPercent { get; init; }

    /// <summary>Memory currently in use in bytes.</summary>
    public long MemoryUsedBytes { get; init; }

    /// <summary>Configured memory limit in bytes.</summary>
    public long MemoryLimitBytes { get; init; }

    /// <summary>Memory utilization percentage.</summary>
    public double MemoryPercent { get; init; }

    /// <summary>Network bytes received during the container lifetime.</summary>
    public long NetworkReceiveBytes { get; init; }

    /// <summary>Network bytes transmitted during the container lifetime.</summary>
    public long NetworkTransmitBytes { get; init; }

    /// <summary>Block bytes read during the container lifetime.</summary>
    public long BlockReadBytes { get; init; }

    /// <summary>Block bytes written during the container lifetime.</summary>
    public long BlockWriteBytes { get; init; }
}

/// <summary>Query options for persisted metric history.</summary>
public sealed record SystemMetricHistoryQuery
{
    /// <summary>Optional metric name.</summary>
    public string? MetricName { get; init; }

    /// <summary>Optional lower timestamp bound.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Maximum records returned.</summary>
    public int Limit { get; init; } = 500;
}
