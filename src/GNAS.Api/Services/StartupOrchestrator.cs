using GNAS.Core;

namespace GNAS.Api.Services;

/// <summary>Background startup orchestrator; failures are only logged to avoid exiting in development environments due to unready external services.</summary>
public sealed class StartupOrchestrator : IHostedService
{
    private readonly IServiceProvider services;
    private readonly ILogger<StartupOrchestrator> logger;

    /// <summary>Initializes the startup orchestrator.</summary>
    public StartupOrchestrator(IServiceProvider services, ILogger<StartupOrchestrator> logger)
    {
        this.services = services;
        this.logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(() => RunAsync(cancellationToken), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            using var scope = services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IModuleHost>().DiscoverAndLoadAsync(ct).ConfigureAwait(false);
            await scope.ServiceProvider.GetRequiredService<IServiceSupervisor>().StartAllAutomaticAsync(ct).ConfigureAwait(false);
            logger.LogInformation("GNAS API startup orchestration completed.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "GNAS API startup orchestration failed, service will continue running in degraded mode.");
        }
    }
}
