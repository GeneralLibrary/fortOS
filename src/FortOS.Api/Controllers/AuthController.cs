using FortOS.Api.Authorization;
using FortOS.Core;
using FortOS.Security.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FortOS.Api.Controllers;

/// <summary>Authentication controller.</summary>
[Route("api/auth")]
public sealed class AuthController : FortOSControllerBase
{
    /// <summary>Local login.</summary>
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<object> Login([FromBody] LoginRequest request, [FromServices] IIdentityService identity, CancellationToken ct)
    {
        var result = await identity.AuthenticateLocalAsync(request.Username, request.Password, ct).ConfigureAwait(false);
        if (!result.Success) return Unauthorized(new { error = result.ErrorMessage, code = "LOGIN_FAILED", traceId = TraceId });
        if (!string.IsNullOrWhiteSpace(request.Totp))
        {
            var totp = await identity.AuthenticateTotpAsync(request.Username, request.Totp, ct).ConfigureAwait(false);
            if (!totp.Success) return Unauthorized(new { error = totp.ErrorMessage, code = "TOTP_FAILED", traceId = TraceId });
        }
        return new { token = result.NasToken, payload = result.TokenPayload };
    }

    /// <summary>
    /// Register local user.
    /// On first startup (no users yet), anonymous calls are allowed to create the first admin account;
    /// once users exist, <see cref="Middleware.NasTokenMiddleware"/> enforces token authentication, and user management capability is checked here.
    /// </summary>
    [BootstrapOnly]
    [HttpPost("register")]
    public async Task<object> Register([FromBody] RegisterRequest request, [FromServices] IIdentityService identity, CancellationToken ct)
    {
        if (HttpContext.Items["NasTokenPayload"] is NasTokenPayload payload && !payload.Capabilities.Satisfies("admin:user:create"))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "User management permission required.", code = "FORBIDDEN", traceId = TraceId });
        }

        var result = await identity.CreateLocalUserAsync(request.Username, request.Password, request.DisplayName, request.Email, ct).ConfigureAwait(false);
        if (!result.Success)
        {
            return BadRequest(new { error = result.ErrorMessage, code = "REGISTER_FAILED", traceId = TraceId });
        }

        return new { success = true, username = request.Username };
    }

    /// <summary>Refresh token: a self-service operation for authenticated users, explicitly labeled with the session refresh capability (default convention is admin:**).</summary>
    [RequiresCapability(NAbilityConstants.SessionRefresh)]
    [HttpPost("refresh")]
    public async Task<object> Refresh([FromServices] ITokenManager tokens, CancellationToken ct)
    {
        var token = OwnerToken;
        return new { token = await tokens.RenewTokenAsync(token, ct).ConfigureAwait(false) };
    }
}
