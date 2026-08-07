using System.Threading.Channels;
using FortOS.Core;
using Proto = FortOS.Proto;
using CoreLogEntry = FortOS.Core.LogEntry;
using ProtoLogEntry = FortOS.Proto.LogEntry;

namespace FortOS.Api.Grpc;

/// <summary>
/// Shared proto <-> core model mapping helpers for the gRPC services.
/// </summary>
internal static class GrpcMappings
{
    /// <summary>Maps a proto log query to the core query model.</summary>
    public static LogQuery ToCore(Proto.LogQueryRequest request) => new()
    {
        Category = Enum.TryParse<LogCategory>(request.Category, true, out var category) ? category : null,
        MinLevel = Enum.TryParse<Microsoft.Extensions.Logging.LogLevel>(request.MinLevel, true, out var level) ? level : null,
        From = request.FromUnix > 0 ? DateTimeOffset.FromUnixTimeSeconds(request.FromUnix) : null,
        To = request.ToUnix > 0 ? DateTimeOffset.FromUnixTimeSeconds(request.ToUnix) : null,
        SearchText = string.IsNullOrWhiteSpace(request.SearchText) ? null : request.SearchText,
        Tags = request.Tags.ToArray(),
        Limit = request.Limit > 0 ? request.Limit : 100,
        Offset = Math.Max(0, request.Offset),
        ServiceId = string.IsNullOrWhiteSpace(request.ServiceId) ? null : request.ServiceId,
        AgentId = string.IsNullOrWhiteSpace(request.AgentId) ? null : request.AgentId,
        TraceId = string.IsNullOrWhiteSpace(request.TraceId) ? null : request.TraceId,
    };

    /// <summary>Maps a core log entry to the proto model.</summary>
    public static ProtoLogEntry ToProto(CoreLogEntry entry)
    {
        var result = new ProtoLogEntry { LogId = entry.LogId, TimestampUnix = entry.Timestamp.ToUnixTimeSeconds(), Category = entry.Category.ToString(), Level = entry.Level.ToString(), SourceComponent = entry.SourceComponent, Message = entry.Message, TraceId = entry.TraceId ?? string.Empty, UserId = entry.UserId ?? string.Empty, AgentId = entry.AgentId ?? string.Empty };
        foreach (var property in entry.Properties) result.Properties[property.Key] = property.Value?.ToString() ?? string.Empty;
        result.Tags.AddRange(entry.Tags);
        return result;
    }

    /// <summary>
    /// Bridges event-bus subscriptions to a gRPC response stream: subscribes to <paramref name="topic"/>,
    /// writes each envelope through <paramref name="write"/>, and tears down the subscription when the client
    /// cancels. Errors thrown by a single event handler do not abort the stream.
    /// </summary>
    public static async Task StreamEventsAsync(IEventBus eventBus, string topic, Func<EventEnvelope, Task> write, CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<EventEnvelope>();
        using var subscription = eventBus.Subscribe(topic, (e, token) => channel.Writer.WriteAsync(e, token).AsTask());
        await foreach (var envelope in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            await write(envelope).ConfigureAwait(false);
        }
    }
}
