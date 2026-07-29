using System.Globalization;
using GNAS.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GNAS.Observability.Metrics;

/// <summary>
/// Coordinates periodic collection, capacity projection, persistence, Prometheus publication,
/// and alert evaluation. It is the only component allowed to publish a monitoring sample.
/// </summary>
public sealed class SystemMetricsService : BackgroundService, ISystemMetricsService
{
    private readonly ISystemMetricsCollector _collector;
    private readonly MetricStore _store;
    private readonly GnasMetrics _prometheus;
    private readonly IAlertEngine _alerts;
    private readonly IGnasConfiguration _configuration;
    private readonly ILogger<SystemMetricsService> _logger;
    private readonly SemaphoreSlim _collectionLock = new(1, 1);
    private readonly Dictionary<string, CapacityTrend> _capacityTrends = new(StringComparer.Ordinal);
    private SystemMetricsSnapshot? _current;
    private DateTimeOffset _lastPrunedAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastPersistedAt = DateTimeOffset.MinValue;

    /// <summary>Initialize the system metrics service.</summary>
    public SystemMetricsService(
        ISystemMetricsCollector collector,
        MetricStore store,
        GnasMetrics prometheus,
        IAlertEngine alerts,
        IGnasConfiguration configuration,
        ILogger<SystemMetricsService> logger)
    {
        _collector = collector;
        _store = store;
        _prometheus = prometheus;
        _alerts = alerts;
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SystemMetricsSnapshot> GetCurrentAsync(CancellationToken ct)
        => Volatile.Read(ref _current) ?? await CollectAndPublishAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public Task<IReadOnlyList<MetricData>> GetHistoryAsync(SystemMetricHistoryQuery query, CancellationToken ct)
        => _store.QueryAsync(query, ct);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await CollectSafelyAsync(stoppingToken).ConfigureAwait(false);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(ReadPositiveInt("monitoring:interval_seconds", 5)));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await CollectSafelyAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task CollectSafelyAsync(CancellationToken ct)
    {
        try
        {
            await CollectAndPublishAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "System metrics collection failed.");
        }
    }

    private async Task<SystemMetricsSnapshot> CollectAndPublishAsync(CancellationToken ct)
    {
        await _collectionLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var collected = await _collector.CollectAsync(ct).ConfigureAwait(false);
            var enriched = EnrichCapacityTrends(collected);
            var metrics = SystemMetricsFlattener.Flatten(enriched);
            Volatile.Write(ref _current, enriched);
            if (enriched.CollectedAt - _lastPersistedAt >= TimeSpan.FromSeconds(ReadPositiveInt("monitoring:history_interval_seconds", 60)))
            {
                try
                {
                    await _store.AppendAsync(metrics, ct).ConfigureAwait(false);
                    _lastPersistedAt = enriched.CollectedAt;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "System metric history persistence failed; live monitoring remains available.");
                }
            }
            try
            {
                _prometheus.RecordSystemSnapshot(metrics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Prometheus system metric publication failed.");
            }
            foreach (var metric in metrics)
            {
                try
                {
                    await _alerts.EvaluateMetricAsync(metric, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Alert evaluation failed for metric {MetricName}.", metric.MetricName);
                }
            }
            try
            {
                await PruneIfDueAsync(enriched.CollectedAt, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "System metric retention pruning failed.");
            }
            return enriched;
        }
        finally
        {
            _collectionLock.Release();
        }
    }

    private SystemMetricsSnapshot EnrichCapacityTrends(SystemMetricsSnapshot snapshot)
    {
        var fileSystems = snapshot.FileSystems.Select(fileSystem =>
        {
            var growth = 0d;
            if (_capacityTrends.TryGetValue(fileSystem.MountPoint, out var previous))
            {
                var elapsed = (snapshot.CollectedAt - previous.CollectedAt).TotalSeconds;
                if (elapsed > 0)
                {
                    var observed = (fileSystem.UsedBytes - previous.UsedBytes) / elapsed;
                    // Smooth short-lived writes so the exhaustion estimate does not oscillate every sample.
                    growth = (previous.SmoothedGrowthBytesPerSecond * 0.8) + (observed * 0.2);
                }
            }
            _capacityTrends[fileSystem.MountPoint] = new CapacityTrend(snapshot.CollectedAt, fileSystem.UsedBytes, growth);
            var estimated = EstimateFullAt(snapshot.CollectedAt, fileSystem.AvailableBytes, growth);
            return fileSystem with { GrowthBytesPerSecond = growth, EstimatedFullAt = estimated };
        }).ToArray();
        return snapshot with { FileSystems = fileSystems };
    }

    internal static DateTimeOffset? EstimateFullAt(DateTimeOffset collectedAt, long availableBytes, double growthBytesPerSecond)
    {
        if (growthBytesPerSecond <= 0 || availableBytes <= 0) return null;
        var secondsUntilFull = availableBytes / growthBytesPerSecond;
        var maximumSeconds = (DateTimeOffset.MaxValue - collectedAt).TotalSeconds;
        return double.IsFinite(secondsUntilFull) && secondsUntilFull > 0 && secondsUntilFull <= maximumSeconds
            ? collectedAt.AddSeconds(secondsUntilFull)
            : null;
    }

    private async Task PruneIfDueAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (now - _lastPrunedAt < TimeSpan.FromHours(1)) return;
        _lastPrunedAt = now;
        var retentionDays = ReadPositiveInt("monitoring:retention_days", 30);
        await _store.PruneAsync(now.AddDays(-retentionDays), ct).ConfigureAwait(false);
    }

    private int ReadPositiveInt(string key, int fallback)
        => int.TryParse(_configuration.GetValue(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : fallback;

    private sealed record CapacityTrend(DateTimeOffset CollectedAt, long UsedBytes, double SmoothedGrowthBytesPerSecond);
}
