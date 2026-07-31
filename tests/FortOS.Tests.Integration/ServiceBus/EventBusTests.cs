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
}
