using FortOS.Core;
using Microsoft.AspNetCore.Mvc;

namespace FortOS.Api.Controllers;

/// <summary>Metrics controller.</summary>
[Route("api/metrics")]
public sealed class MetricsController : FortOSControllerBase
{
    /// <summary>Return the legacy runtime and disk response retained for API compatibility.</summary>
    [HttpGet("current")]
    public async Task<object> Current([FromServices] IDiskManager disks, CancellationToken ct)
    {
        var diskList = await disks.ListDisksAsync(ct).ConfigureAwait(false);
        return new
        {
            gc = new
            {
                totalMemory = GC.GetTotalMemory(false),
                gen0 = GC.CollectionCount(0),
                gen1 = GC.CollectionCount(1),
                gen2 = GC.CollectionCount(2),
            },
            disks = diskList,
        };
    }

    /// <summary>Return the latest typed host, storage, network, service, and container snapshot.</summary>
    [HttpGet("system")]
    public Task<SystemMetricsSnapshot> System([FromServices] ISystemMetricsService metrics, CancellationToken ct)
        => metrics.GetCurrentAsync(ct);

    /// <summary>Return historical metrics.</summary>
    [HttpGet("history")]
    public Task<IReadOnlyList<MetricData>> History(
        [FromServices] ISystemMetricsService metrics,
        [FromQuery] string? metric,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] int limit = 500,
        CancellationToken ct = default)
        => metrics.GetHistoryAsync(new SystemMetricHistoryQuery { MetricName = metric, From = from, Limit = limit }, ct);
}
