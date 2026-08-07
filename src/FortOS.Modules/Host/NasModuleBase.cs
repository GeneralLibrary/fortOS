using FortOS.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FortOS.Modules.Host;

/// <summary>NAS module base implementation, encapsulating common lifecycle scaffolding.</summary>
public abstract class NasModuleBase : INasModule
{
    private ModuleContext? context;
    private ILogger? logger;
    // Lifecycle states: 0=Idle, 1=Initialized, 2=Shutdown. Using an integer field guarantees thread safety.
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
        // Idempotency guard: repeated initialization would create duplicate background services (schedulers/monitoring loops), so it must be rejected.
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
            // On initialization failure, roll back the state so the host can retry (or unload safely).
            Interlocked.Exchange(ref lifecycleState, 0);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task ShutdownAsync(CancellationToken ct)
    {
        // Idempotent: repeated shutdown is harmless (the host reload path may call it multiple times on the same instance).
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
