using System.Security.Cryptography;
using System.Text.Json;
using FortOS.Core;
using Microsoft.Extensions.Logging;

namespace FortOS.Modules.Backup.Services;

/// <summary>Unified backup and restore execution, ensuring leases, states, and reports are not falsely successful.</summary>
public sealed class BackupExecutionService
{
    private const string ManifestName = ".fortos-checksums.json";
    private readonly IProcessManager _process;
    private readonly IEventBus _events;
    private readonly BackupRunHistoryStore _runs;
    private readonly SqliteLeaseService _leases;
    private readonly ILogger<BackupExecutionService> _logger;
    private readonly string _owner = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.CreateVersion7():N}";

    public BackupExecutionService(IProcessManager process, IEventBus events, BackupRunHistoryStore runs, SqliteLeaseService leases, ILogger<BackupExecutionService> logger)
    {
        _process = process;
        _events = events;
        _runs = runs;
        _leases = leases;
        _logger = logger;
    }

    public Task<BackupRunRecord> RunAsync(BackupTask task, CancellationToken ct)
        => ExecuteAsync(task, "run", task.SourcePath, task.Target.BucketOrPath, dryRun: false, ct);

    public Task<BackupRunRecord> RestoreAsync(BackupTask task, string source, string target, bool dryRun, CancellationToken ct)
        => ExecuteAsync(task, "restore", source, target, dryRun, ct);

    private async Task<BackupRunRecord> ExecuteAsync(BackupTask task, string operation, string source, string target, bool dryRun, CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow;
        var runId = Guid.CreateVersion7().ToString();
        var lease = await _leases.AcquireAsync($"backup:{task.TaskId}", _owner, TimeSpan.FromMinutes(15), ct).ConfigureAwait(false);
        if (lease is null) throw new BackupExecutionException("BACKUP_LEASE_CONFLICT", "This backup task is already held by another executor.");

        // 执行期间持续续租：rsync 可能远超 15 分钟租约 TTL，不续租会让租约过期后
        // 被其他执行者抢占，导致同一任务并发双写目标。
        using var leaseRenewal = StartLeaseRenewal(lease, ct);

        var queued = new BackupRunRecord { RunId = runId, TaskId = task.TaskId, Operation = operation, State = BackupRunState.Queued, StartedAt = started, LeaseToken = lease.FencingToken };
        string? checkpoint = null;
        try
        {
            // The queued record is written inside the try so the finally block below always releases
            // the lease — a DB failure here must not leave the task locked for the whole lease TTL.
            await _runs.AppendAsync(queued, ct).ConfigureAwait(false);
            Preflight(source, target, operation, dryRun);
            if (operation == "restore" && !dryRun)
            {
                checkpoint = CreateCheckpointIfNeeded(target, runId);
            }

            var attempts = 0;
            CommandResult result = new() { ExitCode = 1, Stderr = "Not executed." };
            for (; attempts <= Math.Clamp(task.RetryCount, 0, 10); attempts++)
            {
                await _runs.AppendAsync(queued with { State = BackupRunState.Running, Report = new BackupRunReport { AttemptCount = attempts + 1, CheckpointPath = checkpoint } }, ct).ConfigureAwait(false);
                result = await SyncAsync(task, source, target, dryRun, ct).ConfigureAwait(false);
                if (result.ExitCode == 0) break;
                if (attempts < task.RetryCount)
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(300, Math.Max(1, task.RetryBackoffSeconds) * Math.Pow(2, attempts))), ct).ConfigureAwait(false);
            }

            if (result.ExitCode != 0)
                throw new BackupExecutionException("BACKUP_COMMAND_FAILED", string.IsNullOrWhiteSpace(result.Stderr) ? "Backup command failed." : result.Stderr, result);

            string? manifest = null;
            var verified = false;
            if (!dryRun && IsLocal(task.Target.Type))
            {
                if (operation == "run") manifest = await WriteManifestAsync(target, ct).ConfigureAwait(false);
                else verified = await VerifyManifestAsync(source, ct).ConfigureAwait(false);
                if (operation == "restore" && !verified)
                    throw new BackupExecutionException("BACKUP_CHECKSUM_MISMATCH", "Restore source checksum manifest is missing or does not match.");
                await ApplyRetentionAsync(task, target, ct).ConfigureAwait(false);
            }

            // 成功记录必须先落盘并确认，然后才允许清理 checkpoint：若先删 checkpoint
            // 再写记录，任何 DB/IO 异常都会让 catch 分支尝试从已删除的 checkpoint
            // 回滚 —— 恢复数据与备份副本同时丢失（数据双丢）。
            var succeeded = queued with
            {
                State = BackupRunState.Succeeded, Success = true, ExitCode = result.ExitCode, Stdout = result.Stdout, Stderr = result.Stderr,
                FinishedAt = DateTimeOffset.UtcNow, Report = new BackupRunReport { AttemptCount = attempts + 1, ChecksumManifestPath = manifest, ChecksumVerified = verified, CheckpointPath = checkpoint }
            };
            await _runs.AppendAsync(succeeded, ct).ConfigureAwait(false);

            // 记录确认后 checkpoint 才可清理；清理是 best-effort，失败不能掩盖
            // 已确认的成功结果（残留的 checkpoint 由保留策略回收）。
            if (checkpoint is not null) DeleteCheckpointBestEffort(checkpoint);

            // 完成事件是通知语义：发布失败只记日志，绝不能把已成功的备份
            // 拖入 catch 分支触发回滚。
            try
            {
                await _events.PublishAsync($"backup.task.{operation}.completed", "backup.task.completed", JsonSerializer.Serialize(new { task.TaskId, runId, lease = lease.FencingToken }), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Backup run {RunId} succeeded but the completion event could not be published.", runId);
            }
            return succeeded;
        }
        catch (BackupExecutionException ex)
        {
            // 业务失败（命令失败、清单校验失败等）发生在成功记录写入之前，checkpoint
            // 此时必然仍在；仍加存在性检查以防保留策略恰好并发回收。只有真正回滚成功
            // 才标记 RolledBack，否则如实标记 Failed。
            var state = BackupRunState.Failed;
            if (checkpoint is not null && Directory.Exists(checkpoint) && RestoreCheckpoint(target, checkpoint))
            {
                state = BackupRunState.RolledBack;
            }

            var failed = queued with { State = state, FinishedAt = DateTimeOffset.UtcNow, Stderr = ex.Message, ExitCode = ex.Result?.ExitCode ?? 1, Report = new BackupRunReport { ErrorCode = ex.Code, CheckpointPath = checkpoint } };
            await _runs.AppendAsync(failed, ct).ConfigureAwait(false);
            await _events.PublishAsync($"backup.task.{operation}.failed", "backup.task.failed", JsonSerializer.Serialize(new { task.TaskId, runId, code = ex.Code }), ct).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Any unexpected failure (DB/IO error, serializer error, ...) must still close out the
            // run record and remove the checkpoint — otherwise the run stays "Running" forever and
            // the checkpoint directory leaks. Cleanup is best-effort so it cannot mask the original
            // exception.
            // 只有 checkpoint 仍然存在时才尝试回滚：成功路径已在写记录后清理 checkpoint，
            // 若此时它已不存在，如实标记 Failed（标记 RolledBack 会误导审计与用户）。
            var state = BackupRunState.Failed;
            if (checkpoint is not null && Directory.Exists(checkpoint) && RestoreCheckpoint(target, checkpoint))
            {
                state = BackupRunState.RolledBack;
            }

            var failed = queued with { State = state, FinishedAt = DateTimeOffset.UtcNow, Stderr = ex.Message, ExitCode = 1, Report = new BackupRunReport { ErrorCode = "BACKUP_EXECUTION_ERROR", CheckpointPath = checkpoint } };
            try
            {
                await _runs.AppendAsync(failed, CancellationToken.None).ConfigureAwait(false);
                await _events.PublishAsync($"backup.task.{operation}.failed", "backup.task.failed", JsonSerializer.Serialize(new { task.TaskId, runId, code = "BACKUP_EXECUTION_ERROR" }), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception cleanupEx)
            {
                // Best effort only: surface the original failure; log the cleanup failure separately.
                _logger.LogWarning(cleanupEx, "Backup run {RunId} failure cleanup also failed.", runId);
            }

            throw;
        }
        finally
        {
            await _leases.ReleaseAsync(lease, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task<CommandResult> SyncAsync(BackupTask task, string source, string target, bool dryRun, CancellationToken ct)
        => IsLocal(task.Target.Type)
            ? await new RsyncBackupService(_process).SyncAsync(source, target, dryRun, ct, task.ExcludePatterns).ConfigureAwait(false)
            : dryRun
                ? new CommandResult { ExitCode = 2, Stderr = "Cloud backup restore does not support dry-run." }
                : await new CloudBackupService(_process).SyncAsync(source, target, ct).ConfigureAwait(false);

    private static void Preflight(string source, string target, string operation, bool dryRun)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
            throw new BackupExecutionException("BACKUP_PRECHECK_INVALID_PATH", "Source and target paths cannot be empty.");
        if (operation == "restore" && !Directory.Exists(source))
            throw new BackupExecutionException("BACKUP_PRECHECK_SOURCE_MISSING", "Restore source directory does not exist.");
        if (dryRun) return;
        var parent = Path.GetDirectoryName(Path.GetFullPath(target)) ?? target;
        Directory.CreateDirectory(parent);
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(parent))!);
        if (drive.AvailableFreeSpace < 1024 * 1024)
            throw new BackupExecutionException("BACKUP_PRECHECK_SPACE", "Insufficient available space on target.");
    }

    /// <summary>
    /// 启动租约后台续期：每隔 5 分钟调用 <see cref="SqliteLeaseService.RenewAsync"/> 续一次
    /// （TTL 15 分钟，留有充足余量）。续期失败只记警告并继续尝试，不中断主流程。
    /// </summary>
    private IDisposable StartLeaseRenewal(LeaseHandle lease, CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var task = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
            try
            {
                while (await timer.WaitForNextTickAsync(cts.Token).ConfigureAwait(false))
                {
                    try
                    {
                        var renewed = await _leases.RenewAsync(lease, TimeSpan.FromMinutes(15), cts.Token).ConfigureAwait(false);
                        if (!renewed)
                        {
                            _logger.LogWarning("Backup lease {Lease} renewal was rejected; another executor may hold it.", lease.Name);
                        }
                    }
                    catch (OperationCanceledException) when (cts.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Backup lease {Lease} renewal failed; retrying on next tick.", lease.Name);
                    }
                }
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                // 正常取消。
            }
        }, CancellationToken.None);
        return new LeaseRenewalHandle(task, cts);
    }

    /// <summary>租约续期任务句柄：Dispose 时取消续期并等待任务收敛。</summary>
    private sealed class LeaseRenewalHandle(Task task, CancellationTokenSource cts) : IDisposable
    {
        public void Dispose()
        {
            cts.Cancel();
            try
            {
                task.Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex) when (ex is AggregateException or TimeoutException)
            {
                // best-effort：续期任务退出失败不阻塞主流程。
            }
            finally
            {
                cts.Dispose();
            }
        }
    }

    private static bool IsLocal(BackupTargetType type) => type is BackupTargetType.Local or BackupTargetType.RemoteNas;

    private static string? CreateCheckpointIfNeeded(string target, string runId)
    {
        if (!Directory.Exists(target) || !Directory.EnumerateFileSystemEntries(target).Any()) return null;
        var checkpoint = target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + $".fortos-checkpoint-{runId}";
        Directory.Move(target, checkpoint);
        Directory.CreateDirectory(target);
        return checkpoint;
    }

    private static bool RestoreCheckpoint(string target, string checkpoint)
    {
        try
        {
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            Directory.Move(checkpoint, target);
            return true;
        }
        catch { return false; }
    }

    /// <summary>删除 checkpoint 目录；失败仅记警告，不向上传播（清理不得掩盖成功结果）。</summary>
    private void DeleteCheckpointBestEffort(string checkpoint)
    {
        try
        {
            if (Directory.Exists(checkpoint)) Directory.Delete(checkpoint, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to clean up checkpoint directory {Checkpoint}; it will be reclaimed by retention policy.", checkpoint);
        }
    }

    private static async Task<string> WriteManifestAsync(string root, CancellationToken ct)
    {
        if (!Directory.Exists(root)) throw new BackupExecutionException("BACKUP_MANIFEST_TARGET_MISSING", "Backup target directory does not exist.");
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (string.Equals(Path.GetFileName(path), ManifestName, StringComparison.Ordinal)) continue;
            await using var stream = File.OpenRead(path);
            entries[Path.GetRelativePath(root, path)] = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
        }
        var manifest = Path.Combine(root, ManifestName);
        await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(entries), ct).ConfigureAwait(false);
        return manifest;
    }

    private static async Task<bool> VerifyManifestAsync(string root, CancellationToken ct)
    {
        var manifest = Path.Combine(root, ManifestName);
        if (!File.Exists(manifest)) return false;
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(manifest, ct).ConfigureAwait(false));
        if (values is null) return false;
        foreach (var pair in values)
        {
            var path = Path.Combine(root, pair.Key);
            if (!File.Exists(path)) return false;
            await using var stream = File.OpenRead(path);
            if (!string.Equals(pair.Value, Convert.ToHexString(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false)), StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    private static Task ApplyRetentionAsync(BackupTask task, string target, CancellationToken ct)
    {
        if (!Directory.Exists(target)) return Task.CompletedTask;
        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, task.RetentionDays));
        // Checkpoint directories live NEXT TO the target (sibling) as "<target>.fortos-checkpoint-*",
        // so the search pattern must be passed as the pattern argument — concatenating it into the
        // path argument would never match anything and checkpoints would accumulate forever.
        var parent = Path.GetDirectoryName(target);
        var pattern = Path.GetFileName(target) + ".fortos-checkpoint-*";
        if (string.IsNullOrEmpty(parent)) return Task.CompletedTask;
        foreach (var checkpoint in Directory.EnumerateDirectories(parent, pattern))
            if (Directory.GetLastWriteTimeUtc(checkpoint) < cutoff) Directory.Delete(checkpoint, recursive: true);
        return Task.CompletedTask;
    }
}

public sealed class BackupExecutionException : Exception
{
    public BackupExecutionException(string code, string message, CommandResult? result = null) : base(message) { Code = code; Result = result; }
    public string Code { get; }
    public CommandResult? Result { get; }
}
