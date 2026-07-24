namespace GNAS.Core;

/// <summary>NAS 模块基接口。</summary>
public interface INasModule
{
    /// <summary>模块唯一标识。</summary>
    string ModuleId { get; }
    /// <summary>模块显示名称。</summary>
    string DisplayName { get; }
    /// <summary>模块版本。</summary>
    Version Version { get; }
    /// <summary>模块所需能力表达式。</summary>
    IReadOnlyList<string> RequiredCapabilities { get; }
    /// <summary>依赖模块标识。</summary>
    IReadOnlyList<string> Dependencies { get; }
    /// <summary>初始化模块。</summary>
    Task InitializeAsync(ModuleContext context, CancellationToken ct);
    /// <summary>优雅关闭模块。</summary>
    Task ShutdownAsync(CancellationToken ct);
    /// <summary>检查模块健康状态。</summary>
    Task<HealthStatus> CheckHealthAsync(CancellationToken ct);
}
