namespace GORT.Core;

/// <summary>Event bus interface.</summary>
public interface IEventBus
{
    /// <summary>Publish an event envelope.</summary>
    Task PublishAsync(EventEnvelope envelope, CancellationToken ct);
    /// <summary>Publish an event by topic.</summary>
    Task PublishAsync(string topic, string type, string dataJson, CancellationToken ct);
    /// <summary>Subscribe to a topic pattern.</summary>
    IDisposable Subscribe(string topicPattern, Func<EventEnvelope, CancellationToken, Task> handler);
}
