namespace GORT.Core;

/// <summary>Log store interface.</summary>
public interface ILogStore
{
    /// <summary>Append a log entry.</summary>
    Task AppendAsync(LogEntry entry, CancellationToken ct);
    /// <summary>Batch append log entries.</summary>
    Task AppendBatchAsync(IEnumerable<LogEntry> entries, CancellationToken ct);
    /// <summary>Query logs.</summary>
    Task<IReadOnlyList<LogEntry>> QueryAsync(LogQuery query, CancellationToken ct);
}
