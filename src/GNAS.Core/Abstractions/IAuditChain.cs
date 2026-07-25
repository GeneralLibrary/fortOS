namespace GNAS.Core;

/// <summary>不可篡改审计链接口。</summary>
public interface IAuditChain
{
    /// <summary>追加审计日志。</summary>
    Task AppendAsync(LogEntry entry, CancellationToken ct);
    /// <summary>验证链完整性。</summary>
    Task<ChainVerificationResult> VerifyIntegrityAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct);
    /// <summary>导出审计链。</summary>
    Task ExportAsync(DateOnly date, string path, CancellationToken ct);
}
