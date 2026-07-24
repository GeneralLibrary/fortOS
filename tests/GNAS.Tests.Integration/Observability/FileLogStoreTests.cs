using GNAS.Core;
using global::GNAS.Observability.Logging;
using Microsoft.Extensions.Logging;

namespace GNAS.Tests.Integration.Observability;

public sealed class FileLogStoreTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task AppendAndQueryAsync_RoundTripsEntry()
    {
        var root = ObservabilityTestPaths.CreateDataRoot(nameof(AppendAndQueryAsync_RoundTripsEntry));
        var store = new FileLogStore(dataRoot: root);
        var entry = new LogEntry { Category = LogCategory.System, Level = LogLevel.Information, SourceComponent = "test", Message = "roundtrip" };

        await store.AppendAsync(entry, CancellationToken.None);
        var result = await store.QueryAsync(new LogQuery { Category = LogCategory.System, SearchText = "round", Limit = 10 }, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(entry.LogId, result.Single().LogId);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AppendAsync_WhenShardFull_UsesNumberedRotationName()
    {
        var root = ObservabilityTestPaths.CreateDataRoot(nameof(AppendAsync_WhenShardFull_UsesNumberedRotationName));
        var store = new FileLogStore(dataRoot: root, maxShardBytes: 1);

        await store.AppendAsync(new LogEntry { Category = LogCategory.Agent, Level = LogLevel.Information, SourceComponent = "test", Message = "first" }, CancellationToken.None);
        await store.AppendAsync(new LogEntry { Category = LogCategory.Agent, Level = LogLevel.Information, SourceComponent = "test", Message = "second" }, CancellationToken.None);

        var files = Directory.GetFiles(Path.Combine(root, "logs", "agent"), "*.jsonl").Select(Path.GetFileName).ToArray();
        Assert.Contains(files, name => name is not null && name.EndsWith(".1.jsonl", StringComparison.Ordinal));
    }
}
