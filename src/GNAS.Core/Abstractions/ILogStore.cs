namespace GNAS.Core;

/// <summary>日志存储接口。</summary>
public interface ILogStore
{
    /// <summary>追加日志。</summary>
    Task AppendAsync(LogEntry entry, CancellationToken ct);
    /// <summary>批量追加日志。</summary>
    Task AppendBatchAsync(IEnumerable<LogEntry> entries, CancellationToken ct);
    /// <summary>查询日志。</summary>
    Task<IReadOnlyList<LogEntry>> QueryAsync(LogQuery query, CancellationToken ct);
}
