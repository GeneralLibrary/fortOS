using GNAS.Core;

namespace GNAS.Observability.Logging.Stages;

/// <summary>日志分发阶段。</summary>
public sealed class DispatchStage : ILogStage
{
    private readonly IReadOnlyList<ILogStore> _stores;
    private readonly IAuditChain? _auditChain;

    /// <summary>初始化日志分发阶段。</summary>
    public DispatchStage(IEnumerable<ILogStore> stores, IAuditChain? auditChain = null)
    {
        _stores = stores.ToArray();
        _auditChain = auditChain;
    }

    /// <inheritdoc />
    public async Task<LogEntry?> ProcessAsync(LogEntry entry, CancellationToken ct)
    {
        if (entry.Category == LogCategory.Audit && entry.Audit is not null && _auditChain is not null)
        {
            await _auditChain.AppendAsync(entry, ct).ConfigureAwait(false);
        }

        foreach (var store in _stores)
        {
            await store.AppendAsync(entry, ct).ConfigureAwait(false);
        }

        return entry;
    }
}
