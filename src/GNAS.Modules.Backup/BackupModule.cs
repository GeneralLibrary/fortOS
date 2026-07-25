using GNAS.Core;
using GNAS.Modules.Backup.Services;
using GNAS.Modules.Host;
using Microsoft.Extensions.DependencyInjection;

namespace GNAS.Modules.Backup;

/// <summary>备份模块，提供快照、Rsync、云备份与调度。</summary>
public sealed class BackupModule : NasModuleBase
{
    private BackupScheduler? scheduler;
    private BackupTaskStore? taskStore;

    /// <inheritdoc />
    public override string ModuleId => "backup";

    /// <inheritdoc />
    public override string DisplayName => "数据备份";

    /// <inheritdoc />
    public override IReadOnlyList<string> Dependencies => ["storage"];

    /// <inheritdoc />
    public override IReadOnlyList<string> RequiredCapabilities => ["backup:read", "backup:write", "storage:snapshot:write"];

    /// <summary>加载备份任务。</summary>
    public async Task<IReadOnlyList<BackupTask>> ListTasksAsync(CancellationToken ct)
    {
        if (taskStore is null) throw new InvalidOperationException("备份模块尚未初始化。");
        return await taskStore.ListAsync(ct).ConfigureAwait(false);
    }

    /// <summary>保存备份任务。</summary>
    public async Task SaveTasksAsync(IEnumerable<BackupTask> tasks, CancellationToken ct)
    {
        if (taskStore is null) throw new InvalidOperationException("备份模块尚未初始化。");
        await taskStore.ReplaceAllAsync(tasks, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task OnInitializeAsync(CancellationToken ct)
    {
        var database = Services.GetRequiredService<IDatabaseProvider>();
        taskStore = new BackupTaskStore(database);
        await database.InitializeAsync(ct).ConfigureAwait(false);
        var process = Services.GetService(typeof(IProcessManager)) as IProcessManager;
        if (process is not null)
        {
            var executor = Services.GetService<BackupExecutionService>();
            scheduler = executor is null
                ? new BackupScheduler(() => ListTasksAsync(CancellationToken.None), new RsyncBackupService(process), EventBus, Logger)
                : new BackupScheduler(
                    () => ListTasksAsync(CancellationToken.None),
                    async (task, token) => (await executor.RunAsync(task, token).ConfigureAwait(false)).Success,
                    Logger);
            scheduler.Start(ct);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task OnShutdownAsync(CancellationToken ct)
    {
        if (scheduler is not null)
        {
            await scheduler.StopAsync(ct).ConfigureAwait(false);
        }
    }
}
