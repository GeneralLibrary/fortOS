using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FortOS.Api.Controllers;

/// <summary>Health check controller.</summary>
[Route("api/health")]
public sealed class HealthController : FortOSControllerBase
{
    private static readonly DateTimeOffset StartedAt = DateTimeOffset.UtcNow;

    /// <summary>Returns API health status.</summary>
    [AllowAnonymous]
    [HttpGet]
    public object Get() => new { status = "ok", version = typeof(Program).Assembly.GetName().Version?.ToString(), uptime = DateTimeOffset.UtcNow - StartedAt, traceId = TraceId };
}
