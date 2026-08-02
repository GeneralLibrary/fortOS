using System.Collections.Concurrent;
using FortOS.Installer.Core.Session;

namespace FortOS.Installer.Core.Logging;

/// <summary>
/// 内存环形日志缓冲(设计稿 5.1):UI 实时日志 + 完成后落盘。
/// 容量有限,最旧的条目被丢弃。
/// </summary>
public sealed class RingLog
{
    private readonly ConcurrentQueue<InstallLogEntry> _entries = new();
    private readonly int _capacity;
    private int _count;

    public RingLog(int capacity = 500)
    {
        _capacity = capacity;
    }

    /// <summary>新日志条目(供 UI 实时滚动)。</summary>
    public event Action<InstallLogEntry>? EntryAdded;

    public void Info(string message) => Add("INFO", message);

    public void Warn(string message) => Add("WARN", message);

    public void Error(string message) => Add("ERROR", message);

    public void Add(string level, string message)
    {
        var entry = new InstallLogEntry(DateTimeOffset.UtcNow, level, message);
        _entries.Enqueue(entry);
        if (Interlocked.Increment(ref _count) > _capacity)
        {
            _entries.TryDequeue(out _);
            Interlocked.Decrement(ref _count);
        }
        EntryAdded?.Invoke(entry);
    }

    /// <summary>当前全部条目快照(安装完成后落盘)。</summary>
    public IReadOnlyList<InstallLogEntry> Snapshot() => [.. _entries];
}
