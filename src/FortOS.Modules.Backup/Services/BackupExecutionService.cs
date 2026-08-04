using System.Security.Cryptography;
using System.Text.Json;
using FortOS.Core;

namespace FortOS.Modules.Backup.Services;

/// <summary>Unified backup and restore execution, ensuring leases, states, and reports are not falsely successful.</summary>
public sealed class BackupExecutionService
{
    private const string ManifestName = ".fortos-checksums.json";
    private readonly IProcessManager _process;
    private readonly IEventBus _events;
    private readonly BackupRunHistoryStore _runs;
    private readonly SqliteLeaseService _leases;
    private readonly string _owner = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.CreateVersion7():N}";

    public BackupExecutionService(IProcessManager process, IEventBus events, BackupRunHistoryStore runs, SqliteLeaseService leases)
    {
        _process = process;
        _events = events;
        _runs = runs;
        _leases = leases;
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

            if (checkpoint is not null) DeleteCheckpoint(checkpoint);
            var succeeded = queued with
            {
                State = BackupRunState.Succeeded, Success = true, ExitCode = result.ExitCode, Stdout = result.Stdout, Stderr = result.Stderr,
                FinishedAt = DateTimeOffset.UtcNow, Report = new BackupRunReport { AttemptCount = attempts + 1, ChecksumManifestPath = manifest, ChecksumVerified = verified, CheckpointPath = checkpoint }
            };
            await _runs.AppendAsync(succeeded, ct).ConfigureAwait(false);
            await _events.PublishAsync($"backup.task.{operation}.completed", "backup.task.completed", JsonSerializer.Serialize(new { task.TaskId, runId, lease = lease.FencingToken }), ct).ConfigureAwait(false);
            return succeeded;
        }
        catch (BackupExecutionException ex)
        {
            var state = checkpoint is not null && RestoreCheckpoint(target, checkpoint) ? BackupRunState.RolledBack : BackupRunState.Failed;
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
            var state = checkpoint is not null && RestoreCheckpoint(target, checkpoint) ? BackupRunState.RolledBack : BackupRunState.Failed;
            var failed = queued with { State = state, FinishedAt = DateTimeOffset.UtcNow, Stderr = ex.Message, ExitCode = 1, Report = new BackupRunReport { ErrorCode = "BACKUP_EXECUTION_ERROR", CheckpointPath = checkpoint } };
            try
            {
                await _runs.AppendAsync(failed, CancellationToken.None).ConfigureAwait(false);
                await _events.PublishAsync($"backup.task.{operation}.failed", "backup.task.failed", JsonSerializer.Serialize(new { task.TaskId, runId, code = "BACKUP_EXECUTION_ERROR" }), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception cleanupEx)
            {
                // Best effort only: surface the original failure; log the cleanup failure separately.
                Console.Error.WriteLine($"Backup run {runId} failure cleanup also failed: {cleanupEx.Message}");
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
            ? await new RsyncBackupService(_process).SyncAsync(source, target, dryRun, ct).ConfigureAwait(false)
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

    private static void DeleteCheckpoint(string checkpoint)
    {
        if (Directory.Exists(checkpoint)) Directory.Delete(checkpoint, recursive: true);
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
