using System.Collections.Concurrent;
using FortOS.Installer.Core.Session;

namespace FortOS.Installer.Core.Logging;

/// <summary>
/// In-memory ring log buffer (design doc 5.1): real-time UI logs + persistence on completion.
/// The capacity is limited and the oldest entries are dropped.
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

    /// <summary>New log entry (for real-time UI scrolling).</summary>
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

    /// <summary>Snapshot of all current entries (persisted after installation completes).</summary>
    public IReadOnlyList<InstallLogEntry> Snapshot() => [.. _entries];
}
