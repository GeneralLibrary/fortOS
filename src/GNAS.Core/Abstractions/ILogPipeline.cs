namespace GNAS.Core;

/// <summary>Log processing pipeline interface.</summary>
public interface ILogPipeline
{
    /// <summary>Process a structured log entry.</summary>
    Task ProcessAsync(LogEntry entry, CancellationToken ct);
    /// <summary>Process raw log text.</summary>
    Task ProcessRawAsync(string rawText, LogCategory category, string sourceComponent, CancellationToken ct);
}
