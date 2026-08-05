using Microsoft.AspNetCore.Mvc;

namespace FortOS.Api.Controllers;

/// <summary>API controller base class.</summary>
[ApiController]
public abstract class FortOSControllerBase : ControllerBase
{
    /// <summary>Current trace identifier.</summary>
    protected string? TraceId => HttpContext.Items["X-Trace-Id"]?.ToString();

    /// <summary>Current request token.</summary>
    protected string OwnerToken => Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        ? Request.Headers.Authorization.ToString()[7..].Trim()
        : Request.Headers["X-Nas-Token"].ToString();
}
