using System.Collections.Concurrent;
using System.Threading.Channels;
using FortOS.Core;
using Microsoft.Extensions.Logging;

namespace FortOS.ServiceBus.Events;

/// <summary>
/// In-memory channel-based event bus.
/// </summary>
public sealed class EventBus : IEventBus, IDisposable
{
    /// <summary>Per-subscription event queue capacity: drops new events when the consumer is slow/hung instead of unbounded backlogging.</summary>
    private const int SubscriptionCapacity = 256;

    private readonly ConcurrentDictionary<Guid, Subscription> _subscriptions = new();
    private readonly ILogger<EventBus> _logger;
    private bool _disposed;

    /// <summary>
    /// Initialize the event bus.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public EventBus(ILogger<EventBus> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task PublishAsync(EventEnvelope envelope, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (var subscription in _subscriptions.Values)
        {
            if (TopicMatcher.IsMatch(subscription.Pattern, envelope.Topic))
            {
                // Bounded backpressure: when the consumer is slow/hung, a failed TryWrite drops the new event and counts it
                // (rate-limited alert), preventing an unbounded channel from growing memory without limit and taking down the host.
                // Most events are state/progress notifications; dropping an intermediate value beats dropping the whole subscription.
                if (!subscription.Writer.TryWrite(envelope))
                {
                    var dropped = Interlocked.Increment(ref subscription.Dropped);
                    if (dropped == 1 || dropped % 1000 == 0)
                    {
                        _logger.LogWarning("Event subscription {Pattern} is saturated; {Dropped} events have been dropped.", subscription.Pattern, dropped);
                    }
                }
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PublishAsync(string topic, string type, string dataJson, CancellationToken ct)
        => PublishAsync(new EventEnvelope { Topic = topic, Type = type, DataJson = dataJson }, ct);

    /// <inheritdoc />
    public IDisposable Subscribe(string topicPattern, Func<EventEnvelope, CancellationToken, Task> handler)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(topicPattern);
        ArgumentNullException.ThrowIfNull(handler);

        var id = Guid.NewGuid();
        // Bounded queue + DropWrite: when full, new events are dropped (counted and alerted by PublishAsync),
        // preventing slow consumers (email/Webhook/disk writes) from backing up events in memory without limit.
        var channel = Channel.CreateBounded<EventEnvelope>(new BoundedChannelOptions(SubscriptionCapacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

        var cts = new CancellationTokenSource();
        var subscription = new Subscription(id, topicPattern, channel.Writer, cts);
        subscription.Consumer = Task.Run(() => ConsumeAsync(subscription, channel.Reader, handler, cts.Token));
        _subscriptions[id] = subscription;
        return new SubscriptionHandle(this, id);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var id in _subscriptions.Keys)
        {
            Remove(id);
        }
    }

    private async Task ConsumeAsync(Subscription subscription, ChannelReader<EventEnvelope> reader, Func<EventEnvelope, CancellationToken, Task> handler, CancellationToken ct)
    {
        try
        {
            await foreach (var envelope in reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    await handler(envelope, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Event handler failed: Pattern={Pattern}, Topic={Topic}", subscription.Pattern, envelope.Topic);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private void Remove(Guid id)
    {
        if (_subscriptions.TryRemove(id, out var subscription))
        {
            subscription.Cancellation.Cancel();
            subscription.Writer.TryComplete();
            subscription.Cancellation.Dispose();
        }
    }

    private sealed class Subscription(Guid id, string pattern, ChannelWriter<EventEnvelope> writer, CancellationTokenSource cancellation)
    {
        public Guid Id { get; } = id;
        public string Pattern { get; } = pattern;
        public ChannelWriter<EventEnvelope> Writer { get; } = writer;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public Task? Consumer { get; set; }

        /// <summary>Count of events dropped due to a full queue (used for rate-limited alerts).</summary>
        public long Dropped;
    }

    private sealed class SubscriptionHandle : IDisposable
    {
        private readonly EventBus _bus;
        private readonly Guid _id;
        private int _disposed;

        public SubscriptionHandle(EventBus bus, Guid id)
        {
            _bus = bus;
            _id = id;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _bus.Remove(_id);
            }
        }
    }
}

internal static class TopicMatcher
{
    internal static bool IsMatch(string pattern, string topic)
    {
        if (string.Equals(pattern, topic, StringComparison.Ordinal))
        {
            return true;
        }

        var patternSegments = pattern.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var topicSegments = topic.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return Match(patternSegments, 0, topicSegments, 0);
    }

    private static bool Match(string[] pattern, int patternIndex, string[] topic, int topicIndex)
    {
        while (patternIndex < pattern.Length)
        {
            var segment = pattern[patternIndex];
            if (segment == "**")
            {
                if (patternIndex == pattern.Length - 1)
                {
                    return true;
                }

                for (var i = topicIndex; i <= topic.Length; i++)
                {
                    if (Match(pattern, patternIndex + 1, topic, i))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (topicIndex >= topic.Length)
            {
                return false;
            }

            if (segment != "*" && !string.Equals(segment, topic[topicIndex], StringComparison.Ordinal))
            {
                return false;
            }

            patternIndex++;
            topicIndex++;
        }

        return topicIndex == topic.Length;
    }
}
