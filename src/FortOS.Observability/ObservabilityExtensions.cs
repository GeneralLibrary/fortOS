using FortOS.Core;
using FortOS.Observability.Alerts;
using FortOS.Observability.Alerts.Notifiers;
using FortOS.Observability.Audit;
using FortOS.Observability.Logging;
using FortOS.Observability.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace FortOS.Observability;

/// <summary>Observability layer dependency injection extensions.</summary>
public static class ObservabilityExtensions
{
    /// <summary>Register logging, audit chain, alerts, and Serilog integration.</summary>
    public static IServiceCollection AddFortOSObservability(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<FortOSMetrics>();
        services.AddSingleton<MetricStore>();
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

        services.AddSingleton<SystemMetricsService>();
        services.AddSingleton<ISystemMetricsService>(sp => sp.GetRequiredService<SystemMetricsService>());
        services.AddHostedService(sp => sp.GetRequiredService<SystemMetricsService>());

        services.AddHostedService<LazySerilogBootstrapper>();
        services.AddLogging(builder => builder.AddSerilog(dispose: false));
        return services;
    }
}

/// <summary>Background service that lazily configures Serilog using the real service provider.</summary>
public sealed class LazySerilogBootstrapper : IHostedService
{
    private readonly ILogPipeline _pipeline;

    /// <summary>Initialize Serilog lazy bootstrapper.</summary>
    public LazySerilogBootstrapper(ILogPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Async(sink => sink.Sink(new FortOSSerilogSink(_pipeline)))
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
