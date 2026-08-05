using FortOS.Api.Authorization;
using FortOS.Core;
using FortOS.Modules.Backup;
using FortOS.Modules.Backup.Services;
using FortOS.Security.Models;
using Microsoft.AspNetCore.Mvc;

namespace FortOS.Api.Controllers;

/// <summary>
/// Backup task and execution controller.
/// </summary>
[Route("api/backup")]
public sealed class BackupController : FortOSControllerBase
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
    [RequiresCapability("backup:task:read")]
    [HttpGet("tasks")]
    public async Task<IReadOnlyList<BackupTask>> ListTasks(CancellationToken ct)
    {
        return await _backupModule.ListTasksAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Create or update backup task.</summary>
    [RequiresCapability("backup:task:write")]
    [HttpPut("tasks/{taskId}")]
    public async Task<BackupTask> UpsertTask(string taskId, [FromBody] BackupTask task, CancellationToken ct)
    {
        // 源路径与 restore 目标使用同一白名单规则（数据根内），保证「能备份就能恢复」，
        // 两端约束一致（此前 restore 强制白名单但源路径创建时无校验，规则不对称）。
        var dataRoot = FortOS.Modules.Share.Services.ShareValidation.ResolveDataRoot();
        if (!PathSafety.IsPathUnderRoot(task.SourcePath, dataRoot))
        {
            throw new ArgumentException($"Backup source path must be within the data root ({dataRoot}).", nameof(task));
        }

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
    [RequiresCapability("backup:task:write")]
    [HttpDelete("tasks/{taskId}")]
    public async Task<object> DeleteTask(string taskId, CancellationToken ct)
    {
        var tasks = (await _backupModule.ListTasksAsync(ct).ConfigureAwait(false)).ToList();
        var removed = tasks.RemoveAll(t => string.Equals(t.TaskId, taskId, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            throw new ServiceNotFoundException($"Backup task does not exist: {taskId}", "BACKUP_TASK_NOT_FOUND");
        }

        await _backupModule.SaveTasksAsync(tasks, ct).ConfigureAwait(false);
        return new { success = true, taskId };
    }

    /// <summary>Manually execute backup task.</summary>
    [RequiresCapability("backup:task:write")]
    [HttpPost("tasks/{taskId}/run")]
    public async Task<object> RunTask(string taskId, CancellationToken ct)
    {
        var task = await GetTaskAsync(taskId, ct).ConfigureAwait(false);
        var record = await _executor.RunAsync(task, ct).ConfigureAwait(false);
        return new { success = record.Success, taskId = task.TaskId, record.ExitCode, record.Stdout, record.Stderr, runId = record.RunId, state = record.State };
    }

    /// <summary>Read backup run history.</summary>
    [RequiresCapability("backup:task:read")]
    [HttpGet("runs")]
    public async Task<Page<BackupRunRecord>> GetRuns([FromQuery] string? taskId, [FromQuery] int offset = 0, [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        return await _historyStore.QueryPageAsync(taskId, new PageRequest(offset, limit), ct).ConfigureAwait(false);
    }

    /// <summary>Restore backup data to target.</summary>
    [RequiresCapability("backup:task:write")]
    [HttpPost("tasks/{taskId}/restore")]
    public async Task<object> RestoreTask(string taskId, [FromBody] RestoreBackupRequest request, [FromServices] IProcessManager process, CancellationToken ct)
    {
        var task = await GetTaskAsync(taskId, ct).ConfigureAwait(false);
        var source = string.IsNullOrWhiteSpace(request.SourceOverride) ? task.Target.BucketOrPath : request.SourceOverride;
        var target = string.IsNullOrWhiteSpace(request.TargetOverride) ? task.SourcePath : request.TargetOverride;

        // 白名单：restore 目标必须位于数据根之下。校验前先 realpath 解析目标，
        // 否则 /srv/nas/link → /etc 这类 symlink 可通过字符串校验，rsync --delete 会清空数据根外目录。
        var dataRoot = FortOS.Modules.Share.Services.ShareValidation.ResolveDataRoot();
        var resolvedTarget = await ResolveRealPathAsync(process, target, ct).ConfigureAwait(false);
        if (!PathSafety.IsPathUnderRoot(resolvedTarget, dataRoot))
        {
            throw new ArgumentException($"Restore target must be within the data root ({dataRoot}).", nameof(request));
        }

        var record = await _executor.RestoreAsync(task, source, resolvedTarget, request.DryRun, ct).ConfigureAwait(false);
        return new { success = record.Success, taskId = task.TaskId, source, target = resolvedTarget, record.ExitCode, record.Stdout, record.Stderr, runId = record.RunId, state = record.State };
    }

    /// <summary>用 realpath -m 解析路径的真实形式（展开已存在组件的 symlink，不要求路径存在）；失败时退回归一化路径。</summary>
    private static async Task<string> ResolveRealPathAsync(IProcessManager process, string path, CancellationToken ct)
    {
        try
        {
            var result = await process.ExecuteCommandAsync(new ProcessStartConfig
            {
                ExecutablePath = "realpath",
                Arguments = "-m " + QuoteForShell(path),
                TimeoutSeconds = 5,
            }, ct).ConfigureAwait(false);
            if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Stdout))
            {
                return PathSafety.NormalizePath(result.Stdout.Trim());
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // best-effort：realpath 不可用时退回归一化路径（至少保留对 .. 的防护）。
        }

        return PathSafety.NormalizePath(path);
    }

    private static string QuoteForShell(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private async Task<BackupTask> GetTaskAsync(string taskId, CancellationToken ct)
        => (await _backupModule.ListTasksAsync(ct).ConfigureAwait(false)).FirstOrDefault(t => string.Equals(t.TaskId, taskId, StringComparison.OrdinalIgnoreCase))
           ?? throw new ServiceNotFoundException($"Backup task does not exist: {taskId}", "BACKUP_TASK_NOT_FOUND");
}

/// <summary>Restore backup request.</summary>
public sealed record RestoreBackupRequest(string? SourceOverride, string? TargetOverride, bool DryRun);
