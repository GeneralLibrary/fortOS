using GNAS.Core;

namespace GNAS.Observability.Metrics;

/// <summary>
/// Converts the typed snapshot into scalar time series shared by SQLite, Prometheus, and alerts.
/// A single mapping prevents metric names from drifting between those three consumers.
/// </summary>
internal static class SystemMetricsFlattener
{
    internal static IReadOnlyList<MetricData> Flatten(SystemMetricsSnapshot snapshot)
    {
        var result = new List<MetricData>();
        Add(result, snapshot, "system.uptime.seconds", snapshot.Host.Uptime.TotalSeconds, "seconds");
        Add(result, snapshot, "system.load.1", snapshot.Host.LoadAverage1, "load");
        Add(result, snapshot, "system.load.5", snapshot.Host.LoadAverage5, "load");
        Add(result, snapshot, "system.load.15", snapshot.Host.LoadAverage15, "load");
        Add(result, snapshot, "system.cpu.usage.percent", snapshot.Cpu.UsagePercent, "percent");
        Add(result, snapshot, "system.cpu.user.percent", snapshot.Cpu.UserPercent, "percent");
        Add(result, snapshot, "system.cpu.system.percent", snapshot.Cpu.SystemPercent, "percent");
        Add(result, snapshot, "system.cpu.iowait.percent", snapshot.Cpu.IoWaitPercent, "percent");
        Add(result, snapshot, "system.memory.used.bytes", snapshot.Memory.UsedBytes, "bytes");
        Add(result, snapshot, "system.memory.available.bytes", snapshot.Memory.AvailableBytes, "bytes");
        Add(result, snapshot, "system.memory.used.percent", snapshot.Memory.UsedPercent, "percent");
        Add(result, snapshot, "system.swap.used.bytes", snapshot.Memory.SwapUsedBytes, "bytes");
        Add(result, snapshot, "system.swap.used.percent", snapshot.Memory.SwapUsedPercent, "percent");
        Add(result, snapshot, "system.memory.oom_kills", snapshot.Memory.OomKillsSinceLastCollection, "count");
        Add(result, snapshot, "network.tcp.established", snapshot.NetworkStack.EstablishedConnections, "count");
        Add(result, snapshot, "network.tcp.retransmits_per_second", snapshot.NetworkStack.RetransmittedSegmentsPerSecond, "segments_per_second");

        foreach (var protocol in snapshot.ProtocolSessions)
        {
            Add(result, snapshot, "protocol.sessions.active", protocol.ActiveSessions, "count", Dimensions("protocol", protocol.Protocol));
        }

        foreach (var disk in snapshot.Disks)
        {
            var dimensions = Dimensions("disk", disk.Device);
            Add(result, snapshot, "storage.disk.read.bytes_per_second", disk.ReadBytesPerSecond, "bytes_per_second", dimensions);
            Add(result, snapshot, "storage.disk.write.bytes_per_second", disk.WriteBytesPerSecond, "bytes_per_second", dimensions);
            Add(result, snapshot, "storage.disk.read.operations_per_second", disk.ReadOperationsPerSecond, "operations_per_second", dimensions);
            Add(result, snapshot, "storage.disk.write.operations_per_second", disk.WriteOperationsPerSecond, "operations_per_second", dimensions);
            Add(result, snapshot, "storage.disk.latency.milliseconds", disk.AverageLatencyMilliseconds, "milliseconds", dimensions);
            Add(result, snapshot, "storage.disk.utilization.percent", disk.UtilizationPercent, "percent", dimensions);
            if (disk.TemperatureCelsius is { } temperature)
            {
                Add(result, snapshot, "storage.disk.temperature.celsius", temperature, "celsius", dimensions);
            }
            if (TryGetSmartHealthValue(disk.SmartHealth, out var smartHealth))
            {
                Add(result, snapshot, "storage.disk.smart.health", smartHealth, "ratio", dimensions);
            }
        }

        foreach (var network in snapshot.Networks)
        {
            var dimensions = Dimensions("interface", network.Interface);
            Add(result, snapshot, "network.interface.up", network.IsUp ? 1 : 0, "ratio", dimensions);
            Add(result, snapshot, "network.receive.bytes_per_second", network.ReceiveBytesPerSecond, "bytes_per_second", dimensions);
            Add(result, snapshot, "network.transmit.bytes_per_second", network.TransmitBytesPerSecond, "bytes_per_second", dimensions);
            Add(result, snapshot, "network.receive.errors", network.ReceiveErrors, "count", dimensions);
            Add(result, snapshot, "network.transmit.errors", network.TransmitErrors, "count", dimensions);
            Add(result, snapshot, "network.receive.dropped", network.ReceiveDropped, "count", dimensions);
            Add(result, snapshot, "network.transmit.dropped", network.TransmitDropped, "count", dimensions);
        }

        foreach (var fileSystem in snapshot.FileSystems)
        {
            var dimensions = Dimensions("mountpoint", fileSystem.MountPoint);
            Add(result, snapshot, "storage.filesystem.used.bytes", fileSystem.UsedBytes, "bytes", dimensions);
            Add(result, snapshot, "storage.filesystem.available.bytes", fileSystem.AvailableBytes, "bytes", dimensions);
            Add(result, snapshot, "storage.filesystem.used.percent", fileSystem.UsedPercent, "percent", dimensions);
            Add(result, snapshot, "storage.filesystem.growth.bytes_per_second", fileSystem.GrowthBytesPerSecond, "bytes_per_second", dimensions);
            var secondsUntilFull = fileSystem.EstimatedFullAt is { } estimated
                ? Math.Max(0, (estimated - snapshot.CollectedAt).TotalSeconds)
                : double.MaxValue;
            Add(result, snapshot, "storage.filesystem.estimated_full.seconds", secondsUntilFull, "seconds", dimensions);
        }

        foreach (var raid in snapshot.RaidArrays)
        {
            var dimensions = Dimensions("array", raid.Name);
            Add(result, snapshot, "storage.raid.health", raid.Healthy ? 1 : 0, "ratio", dimensions);
            Add(result, snapshot, "storage.raid.active_devices", raid.ActiveDevices, "count", dimensions);
            Add(result, snapshot, "storage.raid.total_devices", raid.TotalDevices, "count", dimensions);
            if (raid.ProgressPercent is { } progress)
            {
                Add(result, snapshot, "storage.raid.operation.progress.percent", progress, "percent", dimensions);
            }
        }

        foreach (var service in snapshot.Services)
        {
            var dimensions = Dimensions("service", service.ServiceId);
            Add(result, snapshot, "service.health", service.State.Equals("active", StringComparison.OrdinalIgnoreCase) ? 1 : 0, "ratio", dimensions);
            Add(result, snapshot, "service.uptime.seconds", service.Uptime.TotalSeconds, "seconds", dimensions);
            Add(result, snapshot, "service.restarts", service.RestartCount, "count", dimensions);
        }

        foreach (var container in snapshot.Containers)
        {
            var dimensions = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["container"] = container.Name,
                ["container_id"] = container.ContainerId,
            };
            Add(result, snapshot, "container.cpu.usage.percent", container.CpuPercent, "percent", dimensions);
            Add(result, snapshot, "container.memory.used.bytes", container.MemoryUsedBytes, "bytes", dimensions);
            Add(result, snapshot, "container.memory.used.percent", container.MemoryPercent, "percent", dimensions);
            Add(result, snapshot, "container.network.receive.bytes", container.NetworkReceiveBytes, "bytes", dimensions);
            Add(result, snapshot, "container.network.transmit.bytes", container.NetworkTransmitBytes, "bytes", dimensions);
            Add(result, snapshot, "container.block.read.bytes", container.BlockReadBytes, "bytes", dimensions);
            Add(result, snapshot, "container.block.write.bytes", container.BlockWriteBytes, "bytes", dimensions);
        }

        return result;
    }

    private static void Add(
        ICollection<MetricData> target,
        SystemMetricsSnapshot snapshot,
        string name,
        double value,
        string unit,
        Dictionary<string, string>? dimensions = null)
        => target.Add(new MetricData
        {
            MetricName = name,
            Value = double.IsFinite(value) ? value : 0,
            Unit = unit,
            Dimensions = dimensions ?? [],
            Timestamp = snapshot.CollectedAt,
        });

    private static Dictionary<string, string> Dimensions(string name, string value)
        => new(StringComparer.Ordinal) { [name] = value };

    private static bool TryGetSmartHealthValue(string? value, out double health)
    {
        health = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.Equals("passed", StringComparison.OrdinalIgnoreCase)
            || value.Equals("ok", StringComparison.OrdinalIgnoreCase)
            || value.Equals("healthy", StringComparison.OrdinalIgnoreCase))
        {
            health = 1;
            return true;
        }
        return value.Contains("fail", StringComparison.OrdinalIgnoreCase)
               || value.Contains("bad", StringComparison.OrdinalIgnoreCase)
               || value.Contains("critical", StringComparison.OrdinalIgnoreCase);
    }
}
