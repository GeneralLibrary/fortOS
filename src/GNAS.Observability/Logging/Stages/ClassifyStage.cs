using GNAS.Core;

namespace GNAS.Observability.Logging.Stages;

/// <summary>日志分类阶段。</summary>
public sealed class ClassifyStage : ILogStage
{
    /// <inheritdoc />
    public Task<LogEntry?> ProcessAsync(LogEntry entry, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var category = entry.Category;
        if (entry.Audit is not null)
        {
            category = LogCategory.Audit;
        }
        else if (entry.Metric is not null)
        {
            category = LogCategory.Metric;
        }

        return Task.FromResult<LogEntry?>(entry with { Category = category });
    }
}
