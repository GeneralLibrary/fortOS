using FortOS.Core;

namespace FortOS.Observability.Logging.Stages;

/// <summary>Log processing stage interface.</summary>
internal interface ILogStage
{
    /// <summary>Process a log entry.</summary>
    Task<LogEntry?> ProcessAsync(LogEntry entry, CancellationToken ct);
}
