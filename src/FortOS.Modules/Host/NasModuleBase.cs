using FortOS.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FortOS.Modules.Host;

/// <summary>NAS module base implementation, encapsulating common lifecycle scaffolding.</summary>
public abstract class NasModuleBase : INasModule
{
    private ModuleContext? context;
    private ILogger? logger;
    // 生命周期状态：0=Idle, 1=Initialized, 2=Shutdown。用整数位保证线程安全。
    private int lifecycleState;

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

    /// <summary>Module runtime context.</summary>
    protected ModuleContext Context => context ?? throw new InvalidOperationException("Module has not been initialized.");

    /// <summary>Module logger.</summary>
    protected ILogger Logger => logger ??= Context.LoggerFactory.CreateLogger(GetType());

    /// <summary>Event bus.</summary>
    protected IEventBus EventBus => Context.EventBus;

    /// <summary>Service provider.</summary>
    protected IServiceProvider Services => Context.Services;

    /// <inheritdoc />
    public async Task InitializeAsync(ModuleContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        // 幂等保护：重复初始化会创建重复的后台服务（调度器/监控循环），必须拒绝。
        if (Interlocked.CompareExchange(ref lifecycleState, 1, 0) != 0)
        {
            throw new InvalidOperationException($"Module {ModuleId} has already been initialized or shut down.");
        }

        this.context = context;
        logger = context.LoggerFactory.CreateLogger(GetType());
        Directory.CreateDirectory(context.DataDirectory);
        try
        {
            await OnInitializeAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            // 初始化失败回滚状态，允许宿主重试（或安全卸载）。
            Interlocked.Exchange(ref lifecycleState, 0);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task ShutdownAsync(CancellationToken ct)
    {
        // 幂等：重复关闭无害（宿主重载路径可能对同一实例调用多次）。
        if (Interlocked.Exchange(ref lifecycleState, 2) == 2)
        {
            return;
        }

        await OnShutdownAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual Task<HealthStatus> CheckHealthAsync(CancellationToken ct) => Task.FromResult(HealthStatus.Healthy);

    /// <summary>Derived module initialization hook.</summary>
    protected virtual Task OnInitializeAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>Derived module shutdown hook.</summary>
    protected virtual Task OnShutdownAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>Resolve required service; returns a clear error when missing.</summary>
    protected T RequiredService<T>() where T : notnull => Services.GetService<T>() ?? throw new InvalidOperationException($"Module {ModuleId} requires service {typeof(T).Name}, which is not registered in DI.");

    /// <summary>Publish JSON events.</summary>
    protected Task PublishAsync(string topic, string type, object payload, CancellationToken ct)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        return EventBus.PublishAsync(topic, type, json, ct);
    }
}
