using GNAS.Core;
using Microsoft.Extensions.Logging;

namespace GNAS.Modules.Backup.Services;

/// <summary>备份调度器，支持 daily HH:mm 与 interval:N 分钟两类简化表达式。</summary>
public sealed class BackupScheduler
{
    private readonly Func<Task<IReadOnlyList<BackupTask>>> tasksProvider;
    private readonly Func<BackupTask, CancellationToken, Task<bool>> execute;
    private readonly ILogger logger;
    private readonly Dictionary<string, DateTimeOffset> lastRuns = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? cts;
    private Task? loopTask;

    /// <summary>创建备份调度器。</summary>
    public BackupScheduler(Func<Task<IReadOnlyList<BackupTask>>> tasksProvider, RsyncBackupService rsync, IEventBus eventBus, ILogger logger)
        : this(tasksProvider, async (task, ct) =>
        {
            var result = await rsync.SyncAsync(task.SourcePath, task.Target.BucketOrPath, dryRun: false, ct).ConfigureAwait(false);
            var success = result.ExitCode == 0;
            await eventBus.PublishAsync(
                success ? "backup.task.completed" : "backup.task.failed",
                success ? "backup.task.completed" : "backup.task.failed",
                System.Text.Json.JsonSerializer.Serialize(new { task.TaskId, result.ExitCode, result.Stderr }),
                ct).ConfigureAwait(false);
            return success;
        }, logger)
    {
    }

    /// <summary>创建使用持久化执行器的调度器。</summary>
    public BackupScheduler(
        Func<Task<IReadOnlyList<BackupTask>>> tasksProvider,
        Func<BackupTask, CancellationToken, Task<bool>> execute,
        ILogger logger)
    {
        this.tasksProvider = tasksProvider;
        this.execute = execute;
        this.logger = logger;
    }

    /// <summary>判断任务在指定时间是否到期。</summary>
    public bool IsDue(BackupTask task, DateTimeOffset now)
    {
        if (!task.Enabled)
        {
            return false;
        }

        if (task.CronExpression.StartsWith("interval:", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(task.CronExpression[9..], out var minutes))
        {
            return !lastRuns.TryGetValue(task.TaskId, out var last) || now - last >= TimeSpan.FromMinutes(minutes);
        }

        if (TimeOnly.TryParse(task.CronExpression, out var time))
        {
            var today = new DateTimeOffset(now.Year, now.Month, now.Day, time.Hour, time.Minute, 0, now.Offset);
            return now >= today && (!lastRuns.TryGetValue(task.TaskId, out var last) || last.Date < now.Date);
        }

        return false;
    }

    /// <summary>启动调度循环。</summary>
    public void Start(CancellationToken ct)
    {
        cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        loopTask = Task.Run(() => RunAsync(cts.Token), CancellationToken.None);
    }

    /// <summary>停止调度循环。</summary>
    public async Task StopAsync(CancellationToken ct)
    {
        if (cts is null || loopTask is null)
        {
            return;
        }

        await cts.CancelAsync().ConfigureAwait(false);
        try
        {
            await loopTask.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            var tasks = await tasksProvider().ConfigureAwait(false);
            foreach (var task in tasks.Where(t => IsDue(t, DateTimeOffset.UtcNow)))
            {
                await RunTaskAsync(task, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task RunTaskAsync(BackupTask task, CancellationToken ct)
    {
        try
        {
            var success = await execute(task, ct).ConfigureAwait(false);
            lastRuns[task.TaskId] = DateTimeOffset.UtcNow;
            if (!success)
            {
                logger.LogWarning("备份任务 {TaskId} 执行失败。", task.TaskId);
            }
        }
        catch (BackupExecutionException ex)
        {
            lastRuns[task.TaskId] = DateTimeOffset.UtcNow;
            logger.LogWarning(ex, "备份任务 {TaskId} 执行失败，错误码 {ErrorCode}。", task.TaskId, ex.Code);
        }
    }
}
