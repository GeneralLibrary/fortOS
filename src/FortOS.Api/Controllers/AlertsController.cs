using FortOS.Core;
using Microsoft.AspNetCore.Mvc;

namespace FortOS.Api.Controllers;

/// <summary>Alert controller.</summary>
[Route("api/alerts")]
public sealed class AlertsController : FortOSControllerBase
{
    /// <summary>List active alerts.</summary>
    [HttpGet]
    public Task<IReadOnlyList<ActiveAlert>> List([FromServices] IAlertEngine engine, CancellationToken ct) => engine.ListActiveAlertsAsync(ct);

    /// <summary>List alert rules.</summary>
    [HttpGet("rules")]
    public Task<IReadOnlyList<AlertRule>> Rules([FromServices] IAlertEngine engine, CancellationToken ct) => engine.ListRulesAsync(ct);

    /// <summary>Add alert rule.</summary>
    [HttpPost("rules")]
    public async Task<object> AddRule([FromBody] AlertRule rule, [FromServices] IAlertEngine engine, CancellationToken ct) { await engine.AddRuleAsync(rule, ct).ConfigureAwait(false); return new { success = true, ruleId = rule.RuleId }; }
}
