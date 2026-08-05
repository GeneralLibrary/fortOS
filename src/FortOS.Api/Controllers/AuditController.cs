using FortOS.Core;
using Microsoft.AspNetCore.Mvc;

namespace FortOS.Api.Controllers;

/// <summary>Audit controller.</summary>
[Route("api/audit")]
public sealed class AuditController : FortOSControllerBase
{
    /// <summary>Verify audit chain.</summary>
    [HttpGet("verify")]
    public Task<ChainVerificationResult> Verify([FromServices] IAuditChain chain, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken ct)
        => chain.VerifyIntegrityAsync(from, to, ct);
}
