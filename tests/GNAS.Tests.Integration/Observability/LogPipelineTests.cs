using System.Text.Json;
using GNAS.Core;
using global::GNAS.Observability.Logging;
using Microsoft.Extensions.Logging;

namespace GNAS.Tests.Integration.Observability;

public sealed class LogPipelineTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task ProcessRawAsync_Json_DispatchesToMemoryStore()
    {
        var memory = new MemoryLogStore();
        await using var pipeline = new LogPipeline([memory]);
        await pipeline.StartAsync(CancellationToken.None);
        var entry = new LogEntry { Category = LogCategory.Agent, Level = LogLevel.Warning, SourceComponent = "agent", Message = "hello" };

        await pipeline.ProcessRawAsync(JsonSerializer.Serialize(entry), LogCategory.System, "raw", CancellationToken.None);
        await WaitForAsync(async () => (await memory.QueryAsync(new LogQuery { Limit = 10 }, CancellationToken.None)).Count == 1);

        var result = await memory.QueryAsync(new LogQuery { Limit = 10 }, CancellationToken.None);
        Assert.Equal("hello", result.Single().Message);
        Assert.Equal(LogCategory.Agent, result.Single().Category);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ProcessAsync_BelowConfiguredLevel_DropsEntry()
    {
        var memory = new MemoryLogStore();
        var config = new TestConfiguration().Set("logging:minlevel", "Warning");
        await using var pipeline = new LogPipeline([memory], config);
        await pipeline.StartAsync(CancellationToken.None);

        await pipeline.ProcessAsync(new LogEntry { Category = LogCategory.System, Level = LogLevel.Information, SourceComponent = "test", Message = "drop" }, CancellationToken.None);
        await Task.Delay(150);

        var result = await memory.QueryAsync(new LogQuery { Limit = 10 }, CancellationToken.None);
        Assert.Empty(result);
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(50);
        }
        Assert.Fail("等待日志管线处理超时。 ");
    }
}
