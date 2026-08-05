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
    /// <summary>每个订阅的事件队列容量：消费者缓慢/挂起时丢弃新事件而非无界积压。</summary>
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
                // 有界背压：消费者缓慢/挂起时 TryWrite 失败则丢弃新事件并计数
                // （限频告警），避免无界 channel 让内存无限增长拖垮宿主。
                // 事件多为状态/进度通知，丢中间值优于丢整个订阅。
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
        // 有界队列 + DropWrite：满时丢弃新事件（由 PublishAsync 计数并告警），
        // 防止慢消费者（邮件/Webhook/磁盘写）导致事件在内存中无限积压。
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

        /// <summary>因队列满而被丢弃的事件计数（用于限频告警）。</summary>
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
