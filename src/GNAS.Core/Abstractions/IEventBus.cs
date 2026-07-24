namespace GNAS.Core;

/// <summary>事件总线接口。</summary>
public interface IEventBus
{
    /// <summary>发布事件信封。</summary>
    Task PublishAsync(EventEnvelope envelope, CancellationToken ct);
    /// <summary>按主题发布事件。</summary>
    Task PublishAsync(string topic, string type, string dataJson, CancellationToken ct);
    /// <summary>订阅主题模式。</summary>
    IDisposable Subscribe(string topicPattern, Func<EventEnvelope, CancellationToken, Task> handler);
}
