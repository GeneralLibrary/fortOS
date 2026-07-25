using System.Text.Json;
using GNAS.Core;
using GNAS.Modules.Backup.Services;
using GNAS.Modules.Host;

namespace GNAS.Modules.Backup;

/// <summary>备份模块，提供快照、Rsync、云备份与调度。</summary>
public sealed class BackupModule : NasModuleBase
{
    private BackupScheduler? scheduler;
    private string TasksPath => Path.Combine(Context.DataDirectory, "config", "backup-tasks.json");

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
        if (!File.Exists(TasksPath))
        {
            return [];
        }

        await using var stream = File.OpenRead(TasksPath);
        return await JsonSerializer.DeserializeAsync<List<BackupTask>>(stream, cancellationToken: ct).ConfigureAwait(false) ?? [];
    }

    /// <summary>保存备份任务。</summary>
    public async Task SaveTasksAsync(IEnumerable<BackupTask> tasks, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(TasksPath)!);
        await using var stream = File.Create(TasksPath);
        await JsonSerializer.SerializeAsync(stream, tasks, new JsonSerializerOptions { WriteIndented = true }, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task OnInitializeAsync(CancellationToken ct)
    {
        var process = Services.GetService(typeof(IProcessManager)) as IProcessManager;
        if (process is not null)
        {
            scheduler = new BackupScheduler(() => ListTasksAsync(CancellationToken.None), new RsyncBackupService(process), EventBus, Logger);
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
