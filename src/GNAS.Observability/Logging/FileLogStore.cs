using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using GNAS.Core;

namespace GNAS.Observability.Logging;

/// <summary>基于 JSONL 文件和日期分片的日志存储。</summary>
public sealed class FileLogStore : ILogStore
{
    private const long DefaultShardBytes = 100L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _root;
    private readonly long _maxShardBytes;
    private readonly ConcurrentDictionary<LogCategory, SemaphoreSlim> _locks = new();
    private DateOnly _lastSweepDate = DateOnly.MinValue;

    /// <summary>初始化文件日志存储。</summary>
    public FileLogStore(IGnasConfiguration? configuration = null, string? dataRoot = null, long maxShardBytes = DefaultShardBytes)
    {
        var root = dataRoot ?? configuration?.GetValue("logging:data_root") ?? Environment.GetEnvironmentVariable("GNAS_DATA_ROOT") ?? "/srv/nas";
        _root = Path.Combine(Path.GetFullPath(root), "logs");
        _maxShardBytes = maxShardBytes;
    }

    /// <inheritdoc />
    public async Task AppendAsync(LogEntry entry, CancellationToken ct)
    {
        var gate = _locks.GetOrAdd(entry.Category, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            if (_lastSweepDate != today)
            {
                _lastSweepDate = today;
                _ = CompressOldFilesAsync(CancellationToken.None);
            }

            var path = GetWritablePath(entry.Category, DateOnly.FromDateTime(entry.Timestamp.LocalDateTime));
            var line = JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine;
            await File.AppendAllTextAsync(path, line, ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
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
    public async Task<IReadOnlyList<LogEntry>> QueryAsync(LogQuery query, CancellationToken ct)
    {
        var results = new List<LogEntry>();
        foreach (var file in EnumerateCandidateFiles(query))
        {
            await foreach (var entry in ReadEntriesAsync(file, ct).ConfigureAwait(false))
            {
                if (MemoryLogStore.Matches(entry, query))
                {
                    results.Add(entry);
                }
            }
        }

        return results.OrderByDescending(entry => entry.Timestamp)
            .Skip(Math.Max(0, query.Offset))
            .Take(Math.Max(0, query.Limit))
            .ToArray();
    }

    /// <summary>压缩超过七天的日志文件。</summary>
    public async Task CompressOldFilesAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_root)) return;
        var cutoff = DateOnly.FromDateTime(DateTime.Now.AddDays(-7));
        foreach (var file in Directory.EnumerateFiles(_root, "*.jsonl", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            if (!TryParseDate(Path.GetFileName(file), out var date) || date >= cutoff) continue;
            var gzPath = file + ".gz";
            if (File.Exists(gzPath))
            {
                File.Delete(file);
                continue;
            }

            await using var source = File.OpenRead(file);
            await using var target = File.Create(gzPath);
            await using var gzip = new GZipStream(target, CompressionLevel.SmallestSize);
            await source.CopyToAsync(gzip, ct).ConfigureAwait(false);
            File.Delete(file);
        }
    }

    private string GetWritablePath(LogCategory category, DateOnly date)
    {
        var dir = Path.Combine(_root, category.ToString().ToLowerInvariant());
        Directory.CreateDirectory(dir);
        var baseName = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var path = Path.Combine(dir, baseName + ".jsonl");
        if (!File.Exists(path) || new FileInfo(path).Length < _maxShardBytes)
        {
            return path;
        }

        for (var shard = 1; ; shard++)
        {
            var shardPath = Path.Combine(dir, $"{baseName}.{shard}.jsonl");
            if (!File.Exists(shardPath) || new FileInfo(shardPath).Length < _maxShardBytes)
            {
                return shardPath;
            }
        }
    }

    private IEnumerable<string> EnumerateCandidateFiles(LogQuery query)
    {
        if (!Directory.Exists(_root)) yield break;
        var categories = query.Category is { } requestedCategory ? new[] { requestedCategory } : Enum.GetValues<LogCategory>();
        foreach (var category in categories)
        {
            var dir = Path.Combine(_root, category.ToString().ToLowerInvariant());
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.jsonl", SearchOption.TopDirectoryOnly))
            {
                if (TryParseDate(Path.GetFileName(file), out var date) && DateInRange(date, query))
                {
                    yield return file;
                }
            }
        }
    }

    private static async IAsyncEnumerable<LogEntry> ReadEntriesAsync(string file, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        using var stream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            LogEntry? entry = null;
            try { entry = JsonSerializer.Deserialize<LogEntry>(line, JsonOptions); }
            catch (JsonException) { }
            if (entry is not null) yield return entry;
        }
    }

    private static bool DateInRange(DateOnly date, LogQuery query)
    {
        if (query.From is { } from && date < DateOnly.FromDateTime(from.LocalDateTime)) return false;
        if (query.To is { } to && date > DateOnly.FromDateTime(to.LocalDateTime)) return false;
        return true;
    }

    private static bool TryParseDate(string fileName, out DateOnly date)
    {
        var parts = fileName.Split('.');
        return DateOnly.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}
