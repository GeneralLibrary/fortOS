using GNAS.Core;

namespace GNAS.Observability.Logging.Stages;

/// <summary>日志处理阶段接口。</summary>
internal interface ILogStage
{
    /// <summary>处理日志条目。</summary>
    Task<LogEntry?> ProcessAsync(LogEntry entry, CancellationToken ct);
}
