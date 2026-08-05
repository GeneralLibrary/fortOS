using System.Text.Json;
using FortOS.Core;
using FortOS.Observability.Logging;
using Microsoft.AspNetCore.Mvc;

namespace FortOS.Api.Controllers;

/// <summary>Log controller.</summary>
[Route("api/logs")]
public sealed class LogsController : FortOSControllerBase
{
    /// <summary>Query logs.</summary>
    [HttpGet]
    public Task<IReadOnlyList<LogEntry>> Query([FromServices] MemoryLogStore logs, [FromQuery] LogQuery query, CancellationToken ct) => logs.QueryAsync(query, ct);

    /// <summary>Stream logs via SSE.</summary>
    [HttpGet("stream")]
    public async Task Stream([FromServices] MemoryLogStore logs, CancellationToken ct)
    {
        Response.Headers.ContentType = "text/event-stream";
        var from = DateTimeOffset.UtcNow;
        while (!ct.IsCancellationRequested)
        {
            var entries = await logs.QueryAsync(new LogQuery { From = from, Limit = 100 }, ct).ConfigureAwait(false);
            foreach (var entry in entries.OrderBy(e => e.Timestamp))
            {
                from = entry.Timestamp.AddTicks(1);
                await Response.WriteAsync($"data: {JsonSerializer.Serialize(entry)}\n\n", ct).ConfigureAwait(false);
            }
            await Response.Body.FlushAsync(ct).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
        }
    }
}
