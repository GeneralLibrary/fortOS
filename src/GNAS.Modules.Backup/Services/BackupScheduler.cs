using GNAS.Core;
using Microsoft.Extensions.Logging;

namespace GNAS.Modules.Backup.Services;

/// <summary>Backup scheduler, supports two simplified expression formats: daily HH:mm and interval:N minutes.</summary>
public sealed class BackupScheduler
{
    private readonly Func<Task<IReadOnlyList<BackupTask>>> tasksProvider;
    private readonly Func<BackupTask, CancellationToken, Task<bool>> execute;
    private readonly ILogger logger;
    private readonly Dictionary<string, DateTimeOffset> lastRuns = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? cts;
    private Task? loopTask;

    /// <summary>Creates a backup scheduler.</summary>
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

    /// <summary>Creates a scheduler using a persistent executor.</summary>
    public BackupScheduler(
        Func<Task<IReadOnlyList<BackupTask>>> tasksProvider,
        Func<BackupTask, CancellationToken, Task<bool>> execute,
        ILogger logger)
    {
        this.tasksProvider = tasksProvider;
        this.execute = execute;
        this.logger = logger;
    }

    /// <summary>Determines whether a task is due at the specified time.</summary>
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

    /// <summary>Starts the scheduling loop.</summary>
    public void Start(CancellationToken ct)
    {
        cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        loopTask = Task.Run(() => RunAsync(cts.Token), CancellationToken.None);
    }

    /// <summary>Stops the scheduling loop.</summary>
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
                logger.LogWarning("Backup task {TaskId} execution failed.", task.TaskId);
            }
        }
        catch (BackupExecutionException ex)
        {
            lastRuns[task.TaskId] = DateTimeOffset.UtcNow;
            logger.LogWarning(ex, "Backup task {TaskId} execution failed, error code {ErrorCode}.", task.TaskId, ex.Code);
        }
    }
}
