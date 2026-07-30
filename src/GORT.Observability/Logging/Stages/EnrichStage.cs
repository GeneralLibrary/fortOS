using System.Diagnostics;
using System.Runtime.InteropServices;
using GORT.Core;

namespace GORT.Observability.Logging.Stages;

/// <summary>Log context enrichment stage.</summary>
public sealed class EnrichStage : ILogStage
{
    /// <inheritdoc />
    public Task<LogEntry?> ProcessAsync(LogEntry entry, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var activity = Activity.Current;
        var enriched = entry with
        {
            TraceId = string.IsNullOrWhiteSpace(entry.TraceId) ? activity?.TraceId.ToString() : entry.TraceId,
            SpanId = string.IsNullOrWhiteSpace(entry.SpanId) ? activity?.SpanId.ToString() : entry.SpanId,
            HostName = string.IsNullOrWhiteSpace(entry.HostName) ? Environment.MachineName : entry.HostName,
            HostArch = string.IsNullOrWhiteSpace(entry.HostArch) ? RuntimeInformation.ProcessArchitecture.ToString() : entry.HostArch
        };
        return Task.FromResult<LogEntry?>(enriched);
    }
}
