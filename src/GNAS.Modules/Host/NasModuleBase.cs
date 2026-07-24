using GNAS.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GNAS.Modules.Host;

/// <summary>NAS 模块基础实现，封装通用生命周期脚手架。</summary>
public abstract class NasModuleBase : INasModule
{
    private ModuleContext? context;
    private ILogger? logger;

    /// <inheritdoc />
    public abstract string ModuleId { get; }

    /// <inheritdoc />
    public abstract string DisplayName { get; }

    /// <inheritdoc />
    public virtual Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public virtual IReadOnlyList<string> RequiredCapabilities => [];

    /// <inheritdoc />
    public virtual IReadOnlyList<string> Dependencies => [];

    /// <summary>模块运行上下文。</summary>
    protected ModuleContext Context => context ?? throw new InvalidOperationException("模块尚未初始化。");

    /// <summary>模块日志记录器。</summary>
    protected ILogger Logger => logger ??= Context.LoggerFactory.CreateLogger(GetType());

    /// <summary>事件总线。</summary>
    protected IEventBus EventBus => Context.EventBus;

    /// <summary>服务提供器。</summary>
    protected IServiceProvider Services => Context.Services;

    /// <inheritdoc />
    public async Task InitializeAsync(ModuleContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        this.context = context;
        logger = context.LoggerFactory.CreateLogger(GetType());
        Directory.CreateDirectory(context.DataDirectory);
        await OnInitializeAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ShutdownAsync(CancellationToken ct)
    {
        await OnShutdownAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual Task<HealthStatus> CheckHealthAsync(CancellationToken ct) => Task.FromResult(HealthStatus.Healthy);

    /// <summary>派生模块初始化钩子。</summary>
    protected virtual Task OnInitializeAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>派生模块关闭钩子。</summary>
    protected virtual Task OnShutdownAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>解析必需服务；缺失时返回清晰错误。</summary>
    protected T RequiredService<T>() where T : notnull => Services.GetService<T>() ?? throw new InvalidOperationException($"模块 {ModuleId} 需要服务 {typeof(T).Name}，但当前 DI 未注册。");

    /// <summary>发布 JSON 事件。</summary>
    protected Task PublishAsync(string topic, string type, object payload, CancellationToken ct)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        return EventBus.PublishAsync(topic, type, json, ct);
    }
}
