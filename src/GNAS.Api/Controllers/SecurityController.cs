using GNAS.Core;
using Microsoft.AspNetCore.Mvc;

namespace GNAS.Api.Controllers;

/// <summary>Admin-only cryptographic key rotation operations.</summary>
[ApiController]
[Route("api/security")]
public sealed class SecurityController : GnasControllerBase
{
    [HttpPost("rotate-signing-key")]
    public async Task<object> RotateSigningKey([FromServices] ITokenManager tokens, [FromServices] ILogPipeline logs, CancellationToken ct)
    {
        var keyId = await tokens.RotateSigningKeyAsync(ct).ConfigureAwait(false);
        await AuditAsync(logs, "security.rotate_signing_key", ct).ConfigureAwait(false);
        return new { keyId, traceId = TraceId };
    }

    [HttpPost("rotate-master-key")]
    public async Task<object> RotateMasterKey([FromServices] IMasterKeyRotationService keys, [FromServices] ILogPipeline logs, CancellationToken ct)
    {
        await keys.RotateMasterKeyAsync(ct).ConfigureAwait(false);
        await AuditAsync(logs, "security.rotate_master_key", ct).ConfigureAwait(false);
        return new { rotated = true, traceId = TraceId };
    }

    private Task AuditAsync(ILogPipeline logs, string action, CancellationToken ct) => logs.ProcessAsync(new LogEntry
    {
        Category = LogCategory.Audit,
        Level = LogLevel.Warning,
        SourceComponent = nameof(SecurityController),
        Message = action,
        TraceId = TraceId,
        Audit = new AuditDetail { Action = action, Resource = "keystore", ResourceType = "security", Granted = true, CurrentHash = string.Empty, ChainSignature = string.Empty },
    }, ct);
}
