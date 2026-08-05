using FortOS.Core;
using FortOS.ServiceBus.Events;
using Microsoft.Extensions.Logging.Abstractions;

namespace FortOS.Tests.Integration.ServiceBus;

public class EventBusTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task PublishAsync_DeliversToSubscriber()
    {
        using var bus = new EventBus(NullLogger<EventBus>.Instance);
        var received = new TaskCompletionSource<EventEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = bus.Subscribe("storage.disk", (envelope, _) =>
        {
            received.TrySetResult(envelope);
            return Task.CompletedTask;
        });

        await bus.PublishAsync("storage.disk", "test", "{}", CancellationToken.None);

        var envelope = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("storage.disk", envelope.Topic);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task WildcardPattern_MatchesSingleSegment()
    {
        using var bus = new EventBus(NullLogger<EventBus>.Instance);
        var received = new TaskCompletionSource<EventEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = bus.Subscribe("agent.*.crashed", (envelope, _) =>
        {
            received.TrySetResult(envelope);
            return Task.CompletedTask;
        });

        await bus.PublishAsync("agent.openclaw.crashed", "test", "{}", CancellationToken.None);

        var envelope = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("agent.openclaw.crashed", envelope.Topic);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Unsubscribe_StopsDelivery()
    {
        using var bus = new EventBus(NullLogger<EventBus>.Instance);
        var deliveries = 0;
        var subscription = bus.Subscribe("agent.*", (_, _) =>
        {
            Interlocked.Increment(ref deliveries);
            return Task.CompletedTask;
        });
        subscription.Dispose();

        await bus.PublishAsync("agent.started", "test", "{}", CancellationToken.None);
        await Task.Delay(100);

        Assert.Equal(0, Volatile.Read(ref deliveries));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task PublishAsync_SlowConsumer_DropsEventsWithoutBlocking()
    {
        // 回归：订阅队列改为有界（256）+ DropWrite 后，慢消费者不再导致无界内存积压，
        // PublishAsync 也绝不阻塞。发布远超容量的事件后，消费者只能处理到队列上限附近。
        using var bus = new EventBus(NullLogger<EventBus>.Instance);
        var gate = new SemaphoreSlim(0);
        var processed = 0;
        using var subscription = bus.Subscribe("load.test", async (_, _) =>
        {
            // 阻塞消费者：首事件进入处理后，后续事件只能在队列中排队/被丢弃。
            await gate.WaitAsync(TimeSpan.FromSeconds(10));
            Interlocked.Increment(ref processed);
        });

        for (var i = 0; i < 1000; i++)
        {
            await bus.PublishAsync("load.test", "test", "{}", CancellationToken.None);
        }

        // 释放消费者，等待排空；队列容量 256，处理数必然远小于发布数。
        gate.Release(1000);
        await Task.Delay(500);

        Assert.InRange(processed, 1, 300);
    }
}
