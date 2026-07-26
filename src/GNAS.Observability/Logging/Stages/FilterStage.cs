using GNAS.Core;
using Microsoft.Extensions.Logging;

namespace GNAS.Observability.Logging.Stages;

/// <summary>Log level filtering stage.</summary>
public sealed class FilterStage : ILogStage
{
    private readonly IGnasConfiguration? _configuration;

    /// <summary>Initialize log level filtering stage.</summary>
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
