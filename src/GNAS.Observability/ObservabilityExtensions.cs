using GNAS.Core;
using GNAS.Observability.Alerts;
using GNAS.Observability.Alerts.Notifiers;
using GNAS.Observability.Audit;
using GNAS.Observability.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace GNAS.Observability;

/// <summary>可观测性层依赖注入扩展。</summary>
public static class ObservabilityExtensions
{
    /// <summary>注册日志、审计链、告警和 Serilog 集成。</summary>
    public static IServiceCollection AddObservability(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<MemoryLogStore>();
        services.AddSingleton<FileLogStore>();
        services.AddSingleton<LokiLogStore>();
        services.AddSingleton<ILogStore>(sp => sp.GetRequiredService<MemoryLogStore>());
        services.AddSingleton<ILogStore>(sp => sp.GetRequiredService<FileLogStore>());
        services.AddSingleton<ILogStore>(sp => sp.GetRequiredService<LokiLogStore>());

        services.AddSingleton<AuditChain>();
        services.AddSingleton<IAuditChain>(sp => sp.GetRequiredService<AuditChain>());
        services.AddHostedService<VerificationBackgroundService>();

        services.AddSingleton<LogPipeline>();
        services.AddSingleton<ILogPipeline>(sp => sp.GetRequiredService<LogPipeline>());
        services.AddHostedService(sp => sp.GetRequiredService<LogPipeline>());

        services.AddSingleton<EmailNotifier>();
        services.AddSingleton<WebhookNotifier>();
        services.AddSingleton<SystemNotifier>();
        services.AddSingleton<INotifier>(sp => sp.GetRequiredService<EmailNotifier>());
        services.AddSingleton<INotifier>(sp => sp.GetRequiredService<WebhookNotifier>());
        services.AddSingleton<INotifier>(sp => sp.GetRequiredService<SystemNotifier>());

        services.AddSingleton<AlertEngine>();
        services.AddSingleton<IAlertEngine>(sp => sp.GetRequiredService<AlertEngine>());
        services.AddHostedService(sp => sp.GetRequiredService<AlertEngine>());

        services.AddHostedService<LazySerilogBootstrapper>();
        services.AddLogging(builder => builder.AddSerilog(dispose: false));
        return services;
    }
}

/// <summary>使用真实服务提供器延迟配置 Serilog 的后台服务。</summary>
public sealed class LazySerilogBootstrapper : IHostedService
{
    private readonly ILogPipeline _pipeline;

    /// <summary>初始化 Serilog 延迟引导器。</summary>
    public LazySerilogBootstrapper(ILogPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Async(sink => sink.Sink(new GnasSerilogSink(_pipeline)))
            .CreateLogger();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        Log.CloseAndFlush();
        return Task.CompletedTask;
    }
}
