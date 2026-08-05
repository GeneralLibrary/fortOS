using FortOS.Core;
using Microsoft.AspNetCore.Mvc;

namespace FortOS.Api.Controllers;

/// <summary>UPS controller.</summary>
[Route("api/ups")]
public sealed class UpsController : FortOSControllerBase
{
    /// <summary>Get UPS status.</summary>
    [HttpGet("status")]
    public async Task<object> Status([FromServices] IProcessManager process, CancellationToken ct)
    {
        var result = await process.ExecuteCommandAsync(new ProcessStartConfig { ExecutablePath = "upsc", Arguments = "ups", TimeoutSeconds = 5 }, ct).ConfigureAwait(false);
        return result.ExitCode == 0 ? new { configured = true, raw = result.Stdout } : new { configured = false, message = "UPS not configured or upsc unavailable.", error = result.Stderr };
    }
}
