using GNAS.Core;
using GNAS.Modules.Backup;
using GNAS.Modules.Backup.Services;
using GNAS.Security.Models;
using Microsoft.AspNetCore.Mvc;

namespace GNAS.Api.Controllers;

/// <summary>
/// Backup task and execution controller.
/// </summary>
[Route("api/backup")]
public sealed class BackupController : GnasControllerBase
{
    private readonly BackupModule _backupModule;
    private readonly IEventBus _eventBus;
    private readonly BackupRunHistoryStore _historyStore;
    private readonly BackupExecutionService _executor;

    /// <summary>Initializes the backup controller.</summary>
    public BackupController(BackupModule backupModule, IEventBus eventBus, BackupRunHistoryStore historyStore, BackupExecutionService executor)
    {
        _backupModule = backupModule;
        _eventBus = eventBus;
        _historyStore = historyStore;
        _executor = executor;
    }

    /// <summary>List backup tasks.</summary>
    [HttpGet("tasks")]
    public async Task<IReadOnlyList<BackupTask>> ListTasks(CancellationToken ct)
    {
        EnsureCapability("backup:task:read");
        return await _backupModule.ListTasksAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Create or update backup task.</summary>
    [HttpPut("tasks/{taskId}")]
    public async Task<BackupTask> UpsertTask(string taskId, [FromBody] BackupTask task, CancellationToken ct)
    {
        EnsureCapability("backup:task:write");
        var normalized = task with { TaskId = taskId };
        var tasks = (await _backupModule.ListTasksAsync(ct).ConfigureAwait(false)).ToList();
        var index = tasks.FindIndex(t => string.Equals(t.TaskId, taskId, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            tasks[index] = normalized;
        }
        else
        {
            tasks.Add(normalized);
        }

        await _backupModule.SaveTasksAsync(tasks, ct).ConfigureAwait(false);
        return normalized;
    }

    /// <summary>Delete backup task.</summary>
    [HttpDelete("tasks/{taskId}")]
    public async Task<object> DeleteTask(string taskId, CancellationToken ct)
    {
        EnsureCapability("backup:task:write");
        var tasks = (await _backupModule.ListTasksAsync(ct).ConfigureAwait(false)).ToList();
        var removed = tasks.RemoveAll(t => string.Equals(t.TaskId, taskId, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            throw new InvalidOperationException($"Backup task does not exist: {taskId}");
        }

        await _backupModule.SaveTasksAsync(tasks, ct).ConfigureAwait(false);
        return new { success = true, taskId };
    }

    /// <summary>Manually execute backup task.</summary>
    [HttpPost("tasks/{taskId}/run")]
    public async Task<object> RunTask(string taskId, CancellationToken ct)
    {
        EnsureCapability("backup:task:write");
        var task = await GetTaskAsync(taskId, ct).ConfigureAwait(false);
        var record = await _executor.RunAsync(task, ct).ConfigureAwait(false);
        return new { success = record.Success, taskId = task.TaskId, record.ExitCode, record.Stdout, record.Stderr, runId = record.RunId, state = record.State };
    }

    /// <summary>Read backup run history.</summary>
    [HttpGet("runs")]
    public async Task<Page<BackupRunRecord>> GetRuns([FromQuery] string? taskId, [FromQuery] int offset = 0, [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        EnsureCapability("backup:task:read");
        return await _historyStore.QueryPageAsync(taskId, new PageRequest(offset, limit), ct).ConfigureAwait(false);
    }

    /// <summary>Restore backup data to target.</summary>
    [HttpPost("tasks/{taskId}/restore")]
    public async Task<object> RestoreTask(string taskId, [FromBody] RestoreBackupRequest request, CancellationToken ct)
    {
        EnsureCapability("backup:task:write");
        var task = await GetTaskAsync(taskId, ct).ConfigureAwait(false);
        var source = string.IsNullOrWhiteSpace(request.SourceOverride) ? task.Target.BucketOrPath : request.SourceOverride;
        var target = string.IsNullOrWhiteSpace(request.TargetOverride) ? task.SourcePath : request.TargetOverride;
        var record = await _executor.RestoreAsync(task, source, target, request.DryRun, ct).ConfigureAwait(false);
        return new { success = record.Success, taskId = task.TaskId, source, target, record.ExitCode, record.Stdout, record.Stderr, runId = record.RunId, state = record.State };
    }

    private async Task<BackupTask> GetTaskAsync(string taskId, CancellationToken ct)
        => (await _backupModule.ListTasksAsync(ct).ConfigureAwait(false)).FirstOrDefault(t => string.Equals(t.TaskId, taskId, StringComparison.OrdinalIgnoreCase))
           ?? throw new InvalidOperationException($"Backup task does not exist: {taskId}");

    private void EnsureCapability(string requiredCapability)
    {
        if (HttpContext.Items["NasTokenPayload"] is not NasTokenPayload payload)
        {
            throw new PermissionDeniedException("Missing authentication context.");
        }

        if (!payload.Capabilities.Satisfies(requiredCapability) && !payload.Capabilities.Satisfies("admin:**"))
        {
            throw new PermissionDeniedException($"Backup operation requires capability {requiredCapability}.");
        }
    }
}

/// <summary>Restore backup request.</summary>
public sealed record RestoreBackupRequest(string? SourceOverride, string? TargetOverride, bool DryRun);
