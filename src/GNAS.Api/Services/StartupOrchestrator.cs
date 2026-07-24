using GNAS.Core;

namespace GNAS.Api.Services;

/// <summary>后台启动编排器；失败只记录日志，避免开发环境因外部服务未就绪而退出。</summary>
public sealed class StartupOrchestrator : IHostedService
{
    private readonly IServiceProvider services;
    private readonly ILogger<StartupOrchestrator> logger;

    /// <summary>初始化启动编排器。</summary>
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
            logger.LogInformation("GNAS API 启动编排完成。");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "GNAS API 启动编排失败，服务将以降级模式继续运行。");
        }
    }
}
