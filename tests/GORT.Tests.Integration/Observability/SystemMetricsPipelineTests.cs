using GORT.Core;
using GORT.Observability.Metrics;

namespace GORT.Tests.Integration.Observability;

public sealed class SystemMetricsPipelineTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void EstimateFullAt_OutOfRangeProjectionReturnsNull()
    {
        var estimate = SystemMetricsService.EstimateFullAt(
            DateTimeOffset.UtcNow,
            availableBytes: 1L << 40,
            growthBytesPerSecond: 0.04);

        Assert.Null(estimate);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Flatten_UnknownSmartStateDoesNotCreateFailureMetric()
    {
        var snapshot = new SystemMetricsSnapshot
        {
            Host = new HostRuntimeMetrics(),
            Cpu = new CpuMetrics(),
            Memory = new MemoryMetrics(),
            Disks =
            [
                new DiskIoMetrics { Device = "sda", SmartHealth = "Unsupported" },
                new DiskIoMetrics { Device = "sdb", SmartHealth = "Passed" },
            ],
        };

        var health = SystemMetricsFlattener.Flatten(snapshot)
            .Where(metric => metric.MetricName == "storage.disk.smart.health")
            .ToArray();

        var metric = Assert.Single(health);
        Assert.Equal("sdb", metric.Dimensions["disk"]);
        Assert.Equal(1, metric.Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Flatten_NoCapacityProjectionEmitsHealthyExhaustionValue()
    {
        var snapshot = new SystemMetricsSnapshot
        {
            Host = new HostRuntimeMetrics(),
            Cpu = new CpuMetrics(),
            Memory = new MemoryMetrics(),
            FileSystems =
            [
                new FileSystemCapacityMetrics { Device = "/dev/sda1", MountPoint = "/srv/nas", EstimatedFullAt = null },
            ],
        };

        var metric = Assert.Single(
            SystemMetricsFlattener.Flatten(snapshot),
            item => item.MetricName == "storage.filesystem.estimated_full.seconds");

        Assert.Equal(double.MaxValue, metric.Value);
    }
}
