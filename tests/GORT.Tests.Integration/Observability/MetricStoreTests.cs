using GORT.Core;
using GORT.Observability.Metrics;

namespace GORT.Tests.Integration.Observability;

public sealed class MetricStoreTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task AppendQueryAndPrune_PreservesDimensionsAndRetention()
    {
        var root = ObservabilityTestPaths.CreateDataRoot("metric-store");
        var store = new MetricStore(new DatabaseProvider(root));
        var oldTimestamp = DateTimeOffset.UtcNow.AddDays(-10);
        var currentTimestamp = DateTimeOffset.UtcNow;
        await store.AppendAsync(
        [
            new MetricData
            {
                MetricName = "storage.disk.temperature.celsius",
                Unit = "celsius",
                Value = 42,
                Timestamp = oldTimestamp,
                Dimensions = new Dictionary<string, string> { ["disk"] = "sda" },
            },
            new MetricData
            {
                MetricName = "storage.disk.temperature.celsius",
                Unit = "celsius",
                Value = 44,
                Timestamp = currentTimestamp,
                Dimensions = new Dictionary<string, string> { ["disk"] = "sdb" },
            },
        ], CancellationToken.None);

        var before = await store.QueryAsync(new SystemMetricHistoryQuery { MetricName = "storage.disk.temperature.celsius" }, CancellationToken.None);
        Assert.Equal(2, before.Count);
        Assert.Equal("sdb", before[0].Dimensions["disk"]);
        var offsetQuery = await store.QueryAsync(new SystemMetricHistoryQuery
        {
            MetricName = "storage.disk.temperature.celsius",
            From = currentTimestamp.AddSeconds(-1).ToOffset(TimeSpan.FromHours(8)),
        }, CancellationToken.None);
        Assert.Single(offsetQuery);

        await store.PruneAsync(DateTimeOffset.UtcNow.AddDays(-1), CancellationToken.None);
        var after = await store.QueryAsync(new SystemMetricHistoryQuery { MetricName = "storage.disk.temperature.celsius" }, CancellationToken.None);
        Assert.Single(after);
        Assert.Equal(44, after[0].Value);
    }
}
