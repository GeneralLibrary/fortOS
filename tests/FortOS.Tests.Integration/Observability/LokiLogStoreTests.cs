using System.Diagnostics;
using System.Net;
using FortOS.Core;
using FortOS.Observability.Logging;
using Microsoft.Extensions.Logging;

namespace FortOS.Tests.Integration.Observability;

/// <summary>
/// LokiLogStore 超时隔离回归测试：Loki 挂起时只取消本次推送（内部 3s 超时），
/// 绝不传播到外部 CancellationToken —— 历史缺陷是全局 HttpClient.Timeout 抛出的
/// TaskCanceledException 被当作外部取消，误杀整个日志管道消费者。
/// </summary>
public sealed class LokiLogStoreTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task AppendAsync_SlowLoki_ReturnsAfterInternalTimeout_WithoutCancellingExternalToken()
    {
        var configuration = new FakeConfiguration(new Dictionary<string, string>
        {
            ["logging:loki:url"] = "http://127.0.0.1:9999",
        });
        // Loki 挂起：响应延迟 30s，远超内部 3s 超时。
        var store = new LokiLogStore(configuration, new HttpClient(new SlowHandler(TimeSpan.FromSeconds(30))));
        var entry = new LogEntry
        {
            Category = LogCategory.System,
            Level = LogLevel.Information,
            SourceComponent = "LokiLogStoreTests",
            Message = "slow loki",
        };
        var external = new CancellationTokenSource();

        var stopwatch = Stopwatch.StartNew();
        await store.AppendAsync(entry, external.Token); // 必须在内部超时后返回，而不是无限等待
        stopwatch.Stop();

        Assert.False(external.IsCancellationRequested); // 外部 token 未被取消
        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(6)); // 内部 3s 超时生效
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AppendAsync_ExternalCancellation_StillPropagates()
    {
        var configuration = new FakeConfiguration(new Dictionary<string, string>
        {
            ["logging:loki:url"] = "http://127.0.0.1:9999",
        });
        var store = new LokiLogStore(configuration, new HttpClient(new SlowHandler(TimeSpan.FromSeconds(30))));
        var entry = new LogEntry
        {
            Category = LogCategory.System,
            Level = LogLevel.Information,
            SourceComponent = "LokiLogStoreTests",
            Message = "cancel me",
        };
        using var external = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        // 外部取消必须继续向上传播（供宿主停机使用），不能被内部超时吞掉。
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.AppendAsync(entry, external.Token));
    }

    /// <summary>最小 IFortOSConfiguration 实现，仅提供测试所需键。</summary>
    private sealed class FakeConfiguration(Dictionary<string, string> values) : IFortOSConfiguration
    {
        public string? GetValue(string key) => values.TryGetValue(key, out var value) ? value : null;
        public string[] GetArray(string key) => [];
        public IReadOnlyDictionary<string, string> GetSection(string key) => new Dictionary<string, string>();
        public Task ReloadAsync(CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>延迟指定时间后才返回的 HTTP handler，模拟挂起的 Loki。</summary>
    private sealed class SlowHandler(TimeSpan delay) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
