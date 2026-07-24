using GNAS.Core;
using Microsoft.Extensions.Logging;

namespace GNAS.Observability.Logging.Stages;

/// <summary>日志级别过滤阶段。</summary>
public sealed class FilterStage : ILogStage
{
    private readonly IGnasConfiguration? _configuration;

    /// <summary>初始化日志级别过滤阶段。</summary>
    public FilterStage(IGnasConfiguration? configuration = null)
    {
        _configuration = configuration;
    }

    /// <inheritdoc />
    public Task<LogEntry?> ProcessAsync(LogEntry entry, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var minLevel = ResolveMinLevel(entry.Category);
        return Task.FromResult(entry.Level < minLevel ? null : entry);
    }

    private LogLevel ResolveMinLevel(LogCategory category)
    {
        var specific = _configuration?.GetValue($"logging:minlevel:{category.ToString().ToLowerInvariant()}");
        var global = _configuration?.GetValue("logging:minlevel");
        if (Enum.TryParse<LogLevel>(specific ?? global, true, out var parsed))
        {
            return parsed;
        }

        return category == LogCategory.System ? LogLevel.Information : LogLevel.Trace;
    }
}
