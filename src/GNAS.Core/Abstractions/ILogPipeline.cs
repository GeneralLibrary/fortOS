namespace GNAS.Core;

/// <summary>日志处理管线接口。</summary>
public interface ILogPipeline
{
    /// <summary>处理结构化日志。</summary>
    Task ProcessAsync(LogEntry entry, CancellationToken ct);
    /// <summary>处理原始日志文本。</summary>
    Task ProcessRawAsync(string rawText, LogCategory category, string sourceComponent, CancellationToken ct);
}
