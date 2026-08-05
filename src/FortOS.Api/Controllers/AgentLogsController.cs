using FortOS.Agent.Collector;
using FortOS.Core;
using Microsoft.AspNetCore.Mvc;

namespace FortOS.Api.Controllers;

/// <summary>Agent push log controller.</summary>
[Route("api/agent/logs")]
public sealed class AgentLogsController : FortOSControllerBase
{
    /// <summary>Receive agent push logs.</summary>
    [HttpPost]
    public async Task<object> Push([FromBody] LogEntry[] entries, [FromServices] AgentLogCollector collector, CancellationToken ct)
    {
        foreach (var entry in entries) await collector.PushAsync(entry, ct).ConfigureAwait(false);
        return new { accepted = entries.Length };
    }
}
