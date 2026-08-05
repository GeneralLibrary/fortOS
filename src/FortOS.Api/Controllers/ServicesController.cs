using FortOS.Core;
using Microsoft.AspNetCore.Mvc;

namespace FortOS.Api.Controllers;

/// <summary>Service controller.</summary>
[Route("api/services")]
public sealed class ServicesController : FortOSControllerBase
{
    /// <summary>List service statuses.</summary>
    [HttpGet]
    public Task<IReadOnlyList<ServiceStatusInfo>> List([FromServices] IServiceSupervisor supervisor, CancellationToken ct) => supervisor.ListStatusesAsync(ct);

    /// <summary>Start service.</summary>
    [HttpPost("{id}/start")]
    public async Task<object> Start(string id, [FromServices] IServiceSupervisor supervisor, CancellationToken ct) { await supervisor.StartAsync(id, ct).ConfigureAwait(false); return new { success = true, serviceId = id }; }

    /// <summary>Stop service.</summary>
    [HttpPost("{id}/stop")]
    public async Task<object> Stop(string id, [FromServices] IServiceSupervisor supervisor, CancellationToken ct) { await supervisor.StopAsync(id, ct).ConfigureAwait(false); return new { success = true, serviceId = id }; }
}
