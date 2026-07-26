using GNAS.Core;

namespace GNAS.Observability.Logging.Stages;

/// <summary>Log processing stage interface.</summary>
internal interface ILogStage
{
    /// <summary>Process a log entry.</summary>
    Task<LogEntry?> ProcessAsync(LogEntry entry, CancellationToken ct);
}
