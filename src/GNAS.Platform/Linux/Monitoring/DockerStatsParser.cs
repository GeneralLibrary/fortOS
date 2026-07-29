using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GNAS.Core;

namespace GNAS.Platform.Linux.Monitoring;

/// <summary>Parses Docker CLI JSON output without depending on the Docker socket protocol.</summary>
internal static partial class DockerStatsParser
{
    internal static IReadOnlyList<ContainerRuntimeMetrics> Parse(string content)
    {
        var result = new List<ContainerRuntimeMetrics>();
        foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var memory = ParsePair(Get(root, "MemUsage"));
            var network = ParsePair(Get(root, "NetIO"));
            var block = ParsePair(Get(root, "BlockIO"));
            result.Add(new ContainerRuntimeMetrics
            {
                ContainerId = Get(root, "ID"),
                Name = Get(root, "Name"),
                CpuPercent = ParsePercent(Get(root, "CPUPerc")),
                MemoryUsedBytes = memory.First,
                MemoryLimitBytes = memory.Second,
                MemoryPercent = ParsePercent(Get(root, "MemPerc")),
                NetworkReceiveBytes = network.First,
                NetworkTransmitBytes = network.Second,
                BlockReadBytes = block.First,
                BlockWriteBytes = block.Second,
            });
        }
        return result;
    }

    internal static long ParseSize(string value)
    {
        var match = SizeRegex().Match(value.Trim());
        if (!match.Success) return 0;
        var amount = double.Parse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
        var multiplier = match.Groups[2].Value.ToUpperInvariant() switch
        {
            "B" => 1d,
            "KB" => 1_000d,
            "KIB" => 1_024d,
            "MB" => 1_000_000d,
            "MIB" => 1_048_576d,
            "GB" => 1_000_000_000d,
            "GIB" => 1_073_741_824d,
            "TB" => 1_000_000_000_000d,
            "TIB" => 1_099_511_627_776d,
            _ => 0d,
        };
        return multiplier <= 0 ? 0 : checked((long)(amount * multiplier));
    }

    private static (long First, long Second) ParsePair(string value)
    {
        var parts = value.Split('/', 2, StringSplitOptions.TrimEntries);
        return (parts.Length > 0 ? ParseSize(parts[0]) : 0, parts.Length > 1 ? ParseSize(parts[1]) : 0);
    }

    private static double ParsePercent(string value)
        => double.TryParse(value.Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    private static string Get(JsonElement element, string name)
        => element.TryGetProperty(name, out var property) ? property.GetString() ?? string.Empty : string.Empty;

    [GeneratedRegex(@"^([\d.]+)\s*([KMGT]?i?B)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SizeRegex();
}
