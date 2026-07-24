using GNAS.Core;
using Microsoft.Extensions.Logging;

namespace GNAS.Observability.Logging;

/// <summary>基于内存环形缓冲区的日志存储。</summary>
public sealed class MemoryLogStore : ILogStore
{
    private readonly object _sync = new();
    private readonly LogEntry[] _buffer;
    private int _next;
    private int _count;

    /// <summary>初始化内存日志存储。</summary>
    public MemoryLogStore(int capacity = 100_000)
    {
        _buffer = new LogEntry[capacity];
    }

    /// <inheritdoc />
    public Task AppendAsync(LogEntry entry, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            _buffer[_next] = entry;
            _next = (_next + 1) % _buffer.Length;
            _count = Math.Min(_count + 1, _buffer.Length);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task AppendBatchAsync(IEnumerable<LogEntry> entries, CancellationToken ct)
    {
        foreach (var entry in entries)
        {
            await AppendAsync(entry, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<LogEntry>> QueryAsync(LogQuery query, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        List<LogEntry> snapshot;
        lock (_sync)
        {
            snapshot = new List<LogEntry>(_count);
            for (var i = 0; i < _count; i++)
            {
                var index = (_next - 1 - i + _buffer.Length) % _buffer.Length;
                snapshot.Add(_buffer[index]);
            }
        }

        var result = snapshot.Where(entry => Matches(entry, query))
            .Skip(Math.Max(0, query.Offset))
            .Take(Math.Max(0, query.Limit))
            .ToArray();
        return Task.FromResult<IReadOnlyList<LogEntry>>(result);
    }

    internal static bool Matches(LogEntry entry, LogQuery query)
    {
        if (query.Category is { } category && entry.Category != category) return false;
        if (query.MinLevel is { } minLevel && entry.Level < minLevel) return false;
        if (query.From is { } from && entry.Timestamp < from) return false;
        if (query.To is { } to && entry.Timestamp > to) return false;
        if (!string.IsNullOrWhiteSpace(query.ServiceId) && entry.ServiceId != query.ServiceId) return false;
        if (!string.IsNullOrWhiteSpace(query.AgentId) && entry.AgentId != query.AgentId) return false;
        if (!string.IsNullOrWhiteSpace(query.TraceId) && entry.TraceId != query.TraceId) return false;
        if (query.Tags is { Length: > 0 } tags && !tags.All(tag => entry.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))) return false;
        if (!string.IsNullOrWhiteSpace(query.SearchText) && !entry.Message.Contains(query.SearchText, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }
}
