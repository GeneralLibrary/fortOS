using System.Text.Json;
using System.Text.Json.Serialization;

namespace GORT.Cli.ApiClient;

/// <summary>Provides common JSON serialization settings for CLI.</summary>
public static class ApiJson
{
    /// <summary>Loose JSON settings for reading REST responses.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

/// <summary>Disk summary data.</summary>
public sealed record DiskDto(string? Path, string? Name, string? Model, string? Serial, string? Status, long? SizeBytes, long? UsedBytes, double? TemperatureCelsius);
/// <summary>Service summary data.</summary>
public sealed record ServiceDto(string? Id, string? Name, string? Status, bool? Enabled);
/// <summary>Agent summary data.</summary>
public sealed record AgentDto(string? Id, string? Name, string? Template, string? Status);
/// <summary>Alert summary data.</summary>
public sealed record AlertDto(string? Id, string? Severity, string? Message, DateTimeOffset? CreatedAt, bool? Acknowledged);
/// <summary>Log entry data.</summary>
public sealed record LogEntryDto(DateTimeOffset? Timestamp, string? Level, string? Category, string? Message, string? TraceId);
/// <summary>Share directory data.</summary>
public sealed record ShareDto(string? Id, string? Name, string? Path, string[]? Protocols, bool? ReadOnly);
/// <summary>Snapshot data.</summary>
public sealed record SnapshotDto(string? Id, string? Target, DateTimeOffset? CreatedAt, string? Status);
/// <summary>Metric summary data.</summary>
public sealed record MetricDto(string? Name, double? Value, string? Unit);
