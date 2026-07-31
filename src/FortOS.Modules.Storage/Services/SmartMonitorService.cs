using System.Text.Json;
using FortOS.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FortOS.Modules.Storage.Services;

/// <summary>SMART monitoring service, checks disk health every 30 minutes and publishes metrics.</summary>
public sealed class SmartMonitorService
{
    private readonly IDiskManager diskManager;
    private readonly IEventBus eventBus;
    private readonly IServiceProvider services;
    private readonly ILogger logger;
    private CancellationTokenSource? cts;
    private Task? loopTask;

    /// <summary>Create the SMART monitoring service.</summary>
    public SmartMonitorService(IDiskManager diskManager, IEventBus eventBus, IServiceProvider services, ILogger logger)
    {
        this.diskManager = diskManager;
        this.eventBus = eventBus;
        this.services = services;
        this.logger = logger;
    }

    /// <summary>Start the monitoring loop.</summary>
    public void Start(CancellationToken ct)
    {
        cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        loopTask = Task.Run(() => RunAsync(cts.Token), CancellationToken.None);
    }

    /// <summary>Stop the monitoring loop.</summary>
    public async Task StopAsync(CancellationToken ct)
    {
        if (cts is null || loopTask is null)
        {
            return;
        }

        await cts.CancelAsync().ConfigureAwait(false);
        try
        {
            await loopTask.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));
        await CheckOnceAsync(ct).ConfigureAwait(false);
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            await CheckOnceAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task CheckOnceAsync(CancellationToken ct)
    {
        IReadOnlyList<DiskInfo> disks;
        try
        {
            disks = await diskManager.ListDisksAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to list disks for SMART check.");
            return;
        }

        foreach (var disk in disks)
        {
            try
            {
                var smart = await diskManager.GetSmartDataAsync(disk.Path, ct).ConfigureAwait(false);
                if (IsFailure(smart.Health))
                {
                    await eventBus.PublishAsync("storage.disk.failed", "storage.disk.failed", JsonSerializer.Serialize(new { disk.Path, smart.Health }), ct).ConfigureAwait(false);
                }

                var temp = smart.TemperatureCelsius ?? disk.TemperatureCelsius;
                if (temp > 0 && services.GetService<ILogPipeline>() is { } pipeline)
                {
                    await pipeline.ProcessAsync(new LogEntry
                    {
                        Category = LogCategory.Metric,
                        Level = LogLevel.Information,
                        SourceComponent = "storage.smart",
                        Message = $"Disk {disk.Path} temperature {temp}°C",
                        Metric = new MetricData
                        {
                            MetricName = "storage.disk.temperature",
                            Unit = "celsius",
                            Value = temp,
                            Dimensions = new Dictionary<string, string> { ["disk"] = disk.Path }
                        }
                    }, ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to read SMART data for disk {DiskPath}.", disk.Path);
            }
        }
    }

    private static bool IsFailure(string health) => health.Contains("fail", StringComparison.OrdinalIgnoreCase)
        || health.Contains("bad", StringComparison.OrdinalIgnoreCase)
        || health.Contains("critical", StringComparison.OrdinalIgnoreCase);
}
