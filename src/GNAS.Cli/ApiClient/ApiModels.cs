using System.Text.Json;
using System.Text.Json.Serialization;

namespace GNAS.Cli.ApiClient;

/// <summary>提供 CLI 共用的 JSON 序列化设置。</summary>
public static class ApiJson
{
    /// <summary>宽松读取 REST 响应的 JSON 设置。</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

/// <summary>磁盘摘要资料。</summary>
public sealed record DiskDto(string? Path, string? Name, string? Model, string? Serial, string? Status, long? SizeBytes, long? UsedBytes, double? TemperatureCelsius);
/// <summary>服务摘要资料。</summary>
public sealed record ServiceDto(string? Id, string? Name, string? Status, bool? Enabled);
/// <summary>代理摘要资料。</summary>
public sealed record AgentDto(string? Id, string? Name, string? Template, string? Status);
/// <summary>警报摘要资料。</summary>
public sealed record AlertDto(string? Id, string? Severity, string? Message, DateTimeOffset? CreatedAt, bool? Acknowledged);
/// <summary>日志条目资料。</summary>
public sealed record LogEntryDto(DateTimeOffset? Timestamp, string? Level, string? Category, string? Message, string? TraceId);
/// <summary>共享目录资料。</summary>
public sealed record ShareDto(string? Id, string? Name, string? Path, string[]? Protocols, bool? ReadOnly);
/// <summary>快照资料。</summary>
public sealed record SnapshotDto(string? Id, string? Target, DateTimeOffset? CreatedAt, string? Status);
/// <summary>指标摘要资料。</summary>
public sealed record MetricDto(string? Name, double? Value, string? Unit);
