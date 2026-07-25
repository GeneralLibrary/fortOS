using GNAS.Core;
using GNAS.Modules.Backup;
using GNAS.Modules.Backup.Services;
using GNAS.Security.Models;
using Microsoft.AspNetCore.Mvc;

namespace GNAS.Api.Controllers;

/// <summary>
/// 备份任务与执行控制器。
/// </summary>
[Route("api/backup")]
public sealed class BackupController : GnasControllerBase
{
    private readonly BackupModule _backupModule;
    private readonly IEventBus _eventBus;
    private readonly BackupRunHistoryStore _historyStore;
    private readonly BackupExecutionService _executor;

    /// <summary>初始化备份控制器。</summary>
    public BackupController(BackupModule backupModule, IEventBus eventBus, BackupRunHistoryStore historyStore, BackupExecutionService executor)
    {
        _backupModule = backupModule;
        _eventBus = eventBus;
        _historyStore = historyStore;
        _executor = executor;
    }

    /// <summary>列出备份任务。</summary>
    [HttpGet("tasks")]
    public async Task<IReadOnlyList<BackupTask>> ListTasks(CancellationToken ct)
    {
        EnsureCapability("backup:task:read");
        return await _backupModule.ListTasksAsync(ct).ConfigureAwait(false);
    }

    /// <summary>创建或更新备份任务。</summary>
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

    /// <summary>删除备份任务。</summary>
    [HttpDelete("tasks/{taskId}")]
    public async Task<object> DeleteTask(string taskId, CancellationToken ct)
    {
        EnsureCapability("backup:task:write");
        var tasks = (await _backupModule.ListTasksAsync(ct).ConfigureAwait(false)).ToList();
        var removed = tasks.RemoveAll(t => string.Equals(t.TaskId, taskId, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            throw new InvalidOperationException($"备份任务不存在：{taskId}");
        }

        await _backupModule.SaveTasksAsync(tasks, ct).ConfigureAwait(false);
        return new { success = true, taskId };
    }

    /// <summary>手动执行备份任务。</summary>
    [HttpPost("tasks/{taskId}/run")]
    public async Task<object> RunTask(string taskId, CancellationToken ct)
    {
        EnsureCapability("backup:task:write");
        var task = await GetTaskAsync(taskId, ct).ConfigureAwait(false);
        var record = await _executor.RunAsync(task, ct).ConfigureAwait(false);
        return new { success = record.Success, taskId = task.TaskId, record.ExitCode, record.Stdout, record.Stderr, runId = record.RunId, state = record.State };
    }

    /// <summary>读取备份运行历史。</summary>
    [HttpGet("runs")]
    public async Task<Page<BackupRunRecord>> GetRuns([FromQuery] string? taskId, [FromQuery] int offset = 0, [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        EnsureCapability("backup:task:read");
        return await _historyStore.QueryPageAsync(taskId, new PageRequest(offset, limit), ct).ConfigureAwait(false);
    }

    /// <summary>恢复备份数据到目标。</summary>
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
           ?? throw new InvalidOperationException($"备份任务不存在：{taskId}");

    private void EnsureCapability(string requiredCapability)
    {
        if (HttpContext.Items["NasTokenPayload"] is not NasTokenPayload payload)
        {
            throw new PermissionDeniedException("缺少认证上下文。");
        }

        if (!payload.Capabilities.Satisfies(requiredCapability) && !payload.Capabilities.Satisfies("admin:**"))
        {
            throw new PermissionDeniedException($"执行备份操作需要能力 {requiredCapability}。");
        }
    }
}

/// <summary>恢复备份请求。</summary>
public sealed record RestoreBackupRequest(string? SourceOverride, string? TargetOverride, bool DryRun);
