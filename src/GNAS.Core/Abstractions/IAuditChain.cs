namespace GNAS.Core;

/// <summary>Tamper-proof audit chain interface.</summary>
public interface IAuditChain
{
    /// <summary>Append an audit log entry.</summary>
    Task AppendAsync(LogEntry entry, CancellationToken ct);
    /// <summary>Verify chain integrity.</summary>
    Task<ChainVerificationResult> VerifyIntegrityAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct);
    /// <summary>Export the audit chain.</summary>
    Task ExportAsync(DateOnly date, string path, CancellationToken ct);
}
