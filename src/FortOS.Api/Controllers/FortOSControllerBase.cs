using FortOS.Api.Middleware;
using Microsoft.AspNetCore.Mvc;

namespace FortOS.Api.Controllers;

/// <summary>API controller base class.</summary>
[ApiController]
public abstract class FortOSControllerBase : ControllerBase
{
    /// <summary>Current trace identifier.</summary>
    protected string? TraceId => HttpContext.Items["X-Trace-Id"]?.ToString();

    /// <summary>Current request token; empty when the request carries no NAS token.</summary>
    protected string OwnerToken => TokenExtraction.FromRequest(Request) ?? string.Empty;
}
