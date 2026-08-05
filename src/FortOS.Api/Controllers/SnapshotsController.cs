using FortOS.Core;
using FortOS.Modules.Backup.Services;
using Microsoft.AspNetCore.Mvc;

namespace FortOS.Api.Controllers;

/// <summary>Snapshot controller.</summary>
[Route("api/snapshots")]
public sealed class SnapshotsController : FortOSControllerBase
{
    /// <summary>Create snapshot.</summary>
    [HttpPost]
    public Task<CommandResult> Create([FromBody] SnapshotRequest request, [FromServices] IProcessManager process, [FromServices] IFileSystem fs, CancellationToken ct)
        => new SnapshotService(process, fs).CreateSnapshotAsync(request.Target, request.Name ?? Guid.CreateVersion7().ToString(), ct);

    /// <summary>List snapshots.</summary>
    [HttpGet]
    public Task<CommandResult> List([FromQuery] string target, [FromServices] IProcessManager process, [FromServices] IFileSystem fs, CancellationToken ct)
        => new SnapshotService(process, fs).ListSnapshotsAsync(target, ct);

    /// <summary>Restore snapshot.</summary>
    [HttpPost("{id}/restore")]
    public Task<CommandResult> Restore(string id, [FromBody] RestoreSnapshotRequest request, [FromServices] IProcessManager process, [FromServices] IFileSystem fs, CancellationToken ct)
        => new SnapshotService(process, fs).RestoreSnapshotAsync(id, request.Target, ct);
}
