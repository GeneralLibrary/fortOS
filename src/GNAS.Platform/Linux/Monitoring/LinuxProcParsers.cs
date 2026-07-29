using System.Globalization;
using System.Text.RegularExpressions;
using GNAS.Core;

namespace GNAS.Platform.Linux.Monitoring;

/// <summary>
/// Parsers for stable Linux procfs formats. Keeping parsing separate from I/O makes rate
/// calculations deterministic and allows captured kernel samples to be tested directly.
/// </summary>
internal static partial class LinuxProcParsers
{
    private const long SectorBytes = 512;

    internal static CpuCounters ParseCpuCounters(string content)
    {
        var line = content.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(value => value.StartsWith("cpu ", StringComparison.Ordinal))
            ?? throw new FormatException("/proc/stat does not contain aggregate CPU counters.");
        var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 8) throw new FormatException("/proc/stat contains incomplete CPU counters.");
        return new CpuCounters(
            ParseLong(fields[1]),
            ParseLong(fields[2]),
            ParseLong(fields[3]),
            ParseLong(fields[4]),
            ParseLong(fields[5]),
            ParseLong(fields[6]),
            ParseLong(fields[7]),
            fields.Length > 8 ? ParseLong(fields[8]) : 0);
    }

    internal static CpuMetrics CalculateCpuMetrics(CpuCounters? previous, CpuCounters current)
    {
        if (previous is null)
        {
            return new CpuMetrics { LogicalProcessorCount = Environment.ProcessorCount };
        }

        var total = current.Total - previous.Value.Total;
        if (total <= 0)
        {
            return new CpuMetrics { LogicalProcessorCount = Environment.ProcessorCount };
        }

        var idle = current.IdleTotal - previous.Value.IdleTotal;
        return new CpuMetrics
        {
            LogicalProcessorCount = Environment.ProcessorCount,
            UsagePercent = Percent(total - idle, total),
            UserPercent = Percent((current.User + current.Nice) - (previous.Value.User + previous.Value.Nice), total),
            SystemPercent = Percent((current.System + current.Irq + current.SoftIrq) - (previous.Value.System + previous.Value.Irq + previous.Value.SoftIrq), total),
            IoWaitPercent = Percent(current.IoWait - previous.Value.IoWait, total),
        };
    }

    internal static MemoryMetrics ParseMemory(string content)
    {
        var values = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var match = MemoryLineRegex().Match(line);
            if (match.Success)
            {
                values[match.Groups[1].Value] = ParseLong(match.Groups[2].Value) * 1024;
            }
        }

        var total = Get(values, "MemTotal");
        var available = Get(values, "MemAvailable");
        var used = Math.Max(0, total - available);
        var swapTotal = Get(values, "SwapTotal");
        var swapUsed = Math.Max(0, swapTotal - Get(values, "SwapFree"));
        return new MemoryMetrics
        {
            TotalBytes = total,
            AvailableBytes = available,
            UsedBytes = used,
            UsedPercent = Percent(used, total),
            SwapTotalBytes = swapTotal,
            SwapUsedBytes = swapUsed,
            SwapUsedPercent = Percent(swapUsed, swapTotal),
        };
    }

    internal static long ParseOomKillCount(string content)
    {
        foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 2 && fields[0].Equals("oom_kill", StringComparison.Ordinal))
            {
                return ParseLong(fields[1]);
            }
        }
        return 0;
    }

    internal static HostRuntimeMetrics ParseRuntime(string uptimeContent, string loadAverageContent, DateTimeOffset now)
    {
        var uptimeSeconds = ParseDouble(uptimeContent.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]);
        var load = loadAverageContent.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var uptime = TimeSpan.FromSeconds(Math.Max(0, uptimeSeconds));
        return new HostRuntimeMetrics
        {
            Uptime = uptime,
            BootedAt = now - uptime,
            LoadAverage1 = load.Length > 0 ? ParseDouble(load[0]) : 0,
            LoadAverage5 = load.Length > 1 ? ParseDouble(load[1]) : 0,
            LoadAverage15 = load.Length > 2 ? ParseDouble(load[2]) : 0,
        };
    }

    internal static IReadOnlyDictionary<string, DiskCounters> ParseDiskCounters(string content)
    {
        var result = new Dictionary<string, DiskCounters>(StringComparer.Ordinal);
        foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 14) continue;
            var device = fields[2];
            if (!Directory.Exists($"/sys/block/{device}")) continue;
            result[device] = new DiskCounters(
                ParseLong(fields[3]),
                ParseLong(fields[5]) * SectorBytes,
                ParseLong(fields[6]),
                ParseLong(fields[7]),
                ParseLong(fields[9]) * SectorBytes,
                ParseLong(fields[10]),
                ParseLong(fields[12]));
        }
        return result;
    }

    internal static IReadOnlyList<DiskIoMetrics> CalculateDiskMetrics(
        IReadOnlyDictionary<string, DiskCounters>? previous,
        IReadOnlyDictionary<string, DiskCounters> current,
        double elapsedSeconds,
        IReadOnlyDictionary<string, SmartData> smart)
    {
        var safeSeconds = Math.Max(elapsedSeconds, 0.001);
        var metrics = new List<DiskIoMetrics>(current.Count);
        foreach (var pair in current.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var old = default(DiskCounters);
            previous?.TryGetValue(pair.Key, out old);
            var hasPrevious = previous?.ContainsKey(pair.Key) == true;
            var reads = hasPrevious ? Delta(pair.Value.ReadOperations, old.ReadOperations) : 0;
            var writes = hasPrevious ? Delta(pair.Value.WriteOperations, old.WriteOperations) : 0;
            var ioOperations = reads + writes;
            smart.TryGetValue(pair.Key, out var health);
            metrics.Add(new DiskIoMetrics
            {
                Device = pair.Key,
                ReadBytesPerSecond = hasPrevious ? Delta(pair.Value.ReadBytes, old.ReadBytes) / safeSeconds : 0,
                WriteBytesPerSecond = hasPrevious ? Delta(pair.Value.WriteBytes, old.WriteBytes) / safeSeconds : 0,
                ReadOperationsPerSecond = reads / safeSeconds,
                WriteOperationsPerSecond = writes / safeSeconds,
                AverageLatencyMilliseconds = ioOperations > 0
                    ? (double)(Delta(pair.Value.ReadMilliseconds, old.ReadMilliseconds) + Delta(pair.Value.WriteMilliseconds, old.WriteMilliseconds)) / ioOperations
                    : 0,
                UtilizationPercent = hasPrevious
                    ? Math.Clamp(Delta(pair.Value.IoMilliseconds, old.IoMilliseconds) / (safeSeconds * 10), 0, 100)
                    : 0,
                TemperatureCelsius = health?.TemperatureCelsius,
                SmartHealth = health?.Health,
            });
        }
        return metrics;
    }

    internal static IReadOnlyDictionary<string, NetworkCounters> ParseNetworkCounters(string content)
    {
        var result = new Dictionary<string, NetworkCounters>(StringComparer.Ordinal);
        foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(2))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0) continue;
            var name = line[..separator].Trim();
            var fields = line[(separator + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 16) continue;
            result[name] = new NetworkCounters(
                ParseLong(fields[0]),
                ParseLong(fields[2]),
                ParseLong(fields[3]),
                ParseLong(fields[8]),
                ParseLong(fields[10]),
                ParseLong(fields[11]));
        }
        return result;
    }

    internal static IReadOnlyList<NetworkTrafficMetrics> CalculateNetworkMetrics(
        IReadOnlyDictionary<string, NetworkCounters>? previous,
        IReadOnlyDictionary<string, NetworkCounters> current,
        double elapsedSeconds,
        string sysRoot = "/sys")
    {
        var safeSeconds = Math.Max(elapsedSeconds, 0.001);
        return current.OrderBy(item => item.Key, StringComparer.Ordinal).Select(pair =>
        {
            var old = default(NetworkCounters);
            previous?.TryGetValue(pair.Key, out old);
            var hasPrevious = previous?.ContainsKey(pair.Key) == true;
            return new NetworkTrafficMetrics
            {
                Interface = pair.Key,
                IsUp = ReadText(Path.Combine(sysRoot, "class", "net", pair.Key, "operstate")).Equals("up", StringComparison.OrdinalIgnoreCase),
                LinkSpeedMbps = TryParseNullableLong(ReadText(Path.Combine(sysRoot, "class", "net", pair.Key, "speed"))),
                ReceiveBytesPerSecond = hasPrevious ? Delta(pair.Value.ReceiveBytes, old.ReceiveBytes) / safeSeconds : 0,
                TransmitBytesPerSecond = hasPrevious ? Delta(pair.Value.TransmitBytes, old.TransmitBytes) / safeSeconds : 0,
                ReceiveErrors = pair.Value.ReceiveErrors,
                TransmitErrors = pair.Value.TransmitErrors,
                ReceiveDropped = pair.Value.ReceiveDropped,
                TransmitDropped = pair.Value.TransmitDropped,
            };
        }).ToArray();
    }

    internal static TcpCounters ParseTcpCounters(string content)
    {
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index + 1 < lines.Length; index++)
        {
            if (!lines[index].StartsWith("Tcp:", StringComparison.Ordinal)
                || !lines[index + 1].StartsWith("Tcp:", StringComparison.Ordinal)) continue;
            var names = lines[index].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var values = lines[index + 1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var establishedIndex = Array.IndexOf(names, "CurrEstab");
            var retransmitIndex = Array.IndexOf(names, "RetransSegs");
            return new TcpCounters(
                establishedIndex > 0 && establishedIndex < values.Length ? ParseLong(values[establishedIndex]) : 0,
                retransmitIndex > 0 && retransmitIndex < values.Length ? ParseLong(values[retransmitIndex]) : 0);
        }
        return default;
    }

    internal static NetworkStackMetrics CalculateNetworkStack(TcpCounters? previous, TcpCounters current, double elapsedSeconds)
        => new()
        {
            EstablishedConnections = current.EstablishedConnections,
            RetransmittedSegmentsPerSecond = previous is null
                ? 0
                : Delta(current.RetransmittedSegments, previous.Value.RetransmittedSegments) / Math.Max(elapsedSeconds, 0.001),
        };

    internal static IReadOnlyList<RaidMetrics> ParseRaid(string content)
    {
        var lines = content.Split('\n');
        var arrays = new List<RaidMetrics>();
        for (var index = 0; index < lines.Length; index++)
        {
            var header = RaidHeaderRegex().Match(lines[index]);
            if (!header.Success) continue;
            var detailLines = new List<string>();
            for (var cursor = index + 1; cursor < lines.Length; cursor++)
            {
                if (string.IsNullOrWhiteSpace(lines[cursor])) break;
                if (!char.IsWhiteSpace(lines[cursor][0])) break;
                detailLines.Add(lines[cursor]);
            }
            var detail = string.Join(' ', detailLines);
            var state = RaidStateRegex().Match(detail);
            var operation = RaidOperationRegex().Match(detail);
            var total = state.Success ? state.Groups[1].Value.Length : 0;
            var active = state.Success ? state.Groups[1].Value.Count(character => character == 'U') : 0;
            arrays.Add(new RaidMetrics
            {
                Name = header.Groups[1].Value,
                Level = header.Groups[2].Value,
                Healthy = total > 0 && active == total,
                ActiveDevices = active,
                TotalDevices = total,
                Operation = operation.Success ? operation.Groups[1].Value : null,
                ProgressPercent = operation.Success ? ParseDouble(operation.Groups[2].Value) : null,
            });
        }
        return arrays;
    }

    private static string ReadText(string path)
    {
        try { return File.ReadAllText(path).Trim(); }
        catch (IOException) { return string.Empty; }
        catch (UnauthorizedAccessException) { return string.Empty; }
    }

    private static long? TryParseNullableLong(string value)
        => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0 ? parsed : null;

    private static long Get(IReadOnlyDictionary<string, long> values, string key)
        => values.TryGetValue(key, out var value) ? value : 0;

    private static long Delta(long current, long previous) => Math.Max(0, current - previous);

    private static double Percent(double value, double total)
        => total <= 0 ? 0 : Math.Clamp(value * 100 / total, 0, 100);

    private static long ParseLong(string value)
        => long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static double ParseDouble(string value)
        => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^([A-Za-z0-9_()]+):\s+(\d+)\s+kB", RegexOptions.CultureInvariant)]
    private static partial Regex MemoryLineRegex();

    [GeneratedRegex(@"^(\S+)\s*:\s*active\s+(\S+)", RegexOptions.CultureInvariant)]
    private static partial Regex RaidHeaderRegex();

    [GeneratedRegex(@"\[([U_]+)\]", RegexOptions.CultureInvariant)]
    private static partial Regex RaidStateRegex();

    [GeneratedRegex(@"\b(resync|recovery|reshape|check)\s*=\s*([\d.]+)%", RegexOptions.CultureInvariant)]
    private static partial Regex RaidOperationRegex();
}

internal readonly record struct CpuCounters(long User, long Nice, long System, long Idle, long IoWait, long Irq, long SoftIrq, long Steal)
{
    internal long Total => User + Nice + System + Idle + IoWait + Irq + SoftIrq + Steal;
    internal long IdleTotal => Idle + IoWait;
}

internal readonly record struct DiskCounters(
    long ReadOperations,
    long ReadBytes,
    long ReadMilliseconds,
    long WriteOperations,
    long WriteBytes,
    long WriteMilliseconds,
    long IoMilliseconds);

internal readonly record struct NetworkCounters(
    long ReceiveBytes,
    long ReceiveErrors,
    long ReceiveDropped,
    long TransmitBytes,
    long TransmitErrors,
    long TransmitDropped);

internal readonly record struct TcpCounters(long EstablishedConnections, long RetransmittedSegments);
