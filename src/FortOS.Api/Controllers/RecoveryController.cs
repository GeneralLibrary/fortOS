using System.Text.Json;
using FortOS.Core;
using FortOS.Modules.Backup.Services;
using Microsoft.AspNetCore.Mvc;

namespace FortOS.Api.Controllers;

/// <summary>Recovery controller.</summary>
[Route("api/recovery")]
public sealed class RecoveryController : FortOSControllerBase
{
    /// <summary>Start recovery process and execute immediately.</summary>
    [HttpPost("start")]
    public async Task<object> Start([FromBody] RecoveryRequest request, [FromServices] IEventBus bus, [FromServices] IProcessManager process, [FromServices] IFileSystem fileSystem, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Target);
        var mode = ResolveRecoveryMode(request);
        await bus.PublishAsync("system.recovery.started", "system.recovery.started", JsonSerializer.Serialize(request), ct).ConfigureAwait(false);
        var result = mode switch
        {
            "snapshot" => await RunSnapshotRecoveryAsync(request, process, fileSystem, ct).ConfigureAwait(false),
            "rsync" => await RunRsyncRecoveryAsync(request, process, ct).ConfigureAwait(false),
            _ => throw new ArgumentException($"Unsupported recovery mode: {mode}", nameof(request)),
        };
        var success = result.ExitCode == 0;
        await bus.PublishAsync(
            success ? "system.recovery.completed" : "system.recovery.failed",
            success ? "system.recovery.completed" : "system.recovery.failed",
            JsonSerializer.Serialize(new { request.Target, mode, result.ExitCode, result.Stdout, result.Stderr }),
            ct).ConfigureAwait(false);
        return new
        {
            success,
            mode,
            request.Target,
            result.ExitCode,
            result.Stdout,
            result.Stderr
        };
    }

    private static string ResolveRecoveryMode(RecoveryRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Mode))
        {
            return request.Mode.Trim().ToLowerInvariant();
        }

        return string.IsNullOrWhiteSpace(request.SnapshotId) ? "rsync" : "snapshot";
    }

    private static Task<CommandResult> RunSnapshotRecoveryAsync(RecoveryRequest request, IProcessManager process, IFileSystem fileSystem, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.SnapshotId))
        {
            throw new ArgumentException("Snapshot mode must provide snapshotId.", nameof(request));
        }

        var snapshots = new SnapshotService(process, fileSystem);
        return snapshots.RestoreSnapshotAsync(request.SnapshotId, request.Target, ct);
    }

    private static Task<CommandResult> RunRsyncRecoveryAsync(RecoveryRequest request, IProcessManager process, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Source))
        {
            throw new ArgumentException("Rsync mode must provide source.", nameof(request));
        }

        var rsync = new RsyncBackupService(process);
        return rsync.SyncAsync(request.Source, request.Target, request.DryRun, ct);
    }
}
