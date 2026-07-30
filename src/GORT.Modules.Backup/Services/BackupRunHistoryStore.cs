using System.Text.Json;
using GORT.Core;

namespace GORT.Modules.Backup.Services;

/// <summary>SQLite-persisted backup task and run record store, migrates legacy JSON on first read.</summary>
public sealed class BackupRunHistoryStore
{
    private readonly IDatabaseProvider _database;
    private readonly string _legacyHistoryPath;
    private readonly SemaphoreSlim _migrationGate = new(1, 1);

    public BackupRunHistoryStore(IDatabaseProvider? database = null)
    {
        _database = database ?? new DatabaseProvider();
        var root = Environment.GetEnvironmentVariable("GORT_DATA_ROOT");
        _legacyHistoryPath = Path.Combine(string.IsNullOrWhiteSpace(root) ? "/srv/nas" : root, "config", "backup-runs.json");
    }

    public async Task AppendAsync(BackupRunRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);
        await EnsureMigratedAsync(ct).ConfigureAwait(false);
        await using var connection = await _database.GetConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
INSERT INTO backup_runs(run_id, task_id, state, started_at, finished_at, lease_token, report_json)
VALUES($id, $task, $state, $started, $finished, $token, $report)
ON CONFLICT(run_id) DO UPDATE SET state = excluded.state, finished_at = excluded.finished_at, lease_token = excluded.lease_token, report_json = excluded.report_json;
""";
        command.Parameters.AddWithValue("$id", record.RunId);
        command.Parameters.AddWithValue("$task", record.TaskId);
        command.Parameters.AddWithValue("$state", record.State.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("$started", record.StartedAt.ToString("O"));
        command.Parameters.AddWithValue("$finished", (object?)record.FinishedAt?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$token", (object?)record.LeaseToken ?? DBNull.Value);
        command.Parameters.AddWithValue("$report", JsonSerializer.Serialize(record));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BackupRunRecord>> QueryAsync(string? taskId, int limit, CancellationToken ct)
    {
        await EnsureMigratedAsync(ct).ConfigureAwait(false);
        await using var connection = await _database.GetConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
SELECT report_json FROM backup_runs
WHERE ($task IS NULL OR task_id = $task)
ORDER BY started_at DESC LIMIT $limit;
""";
        command.Parameters.AddWithValue("$task", (object?)taskId ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, PageRequest.MaximumLimit));
        var records = new List<BackupRunRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var record = JsonSerializer.Deserialize<BackupRunRecord>(reader.GetString(0));
            if (record is not null) records.Add(record);
        }
        return records;
    }

    public async Task<Page<BackupRunRecord>> QueryPageAsync(string? taskId, PageRequest request, CancellationToken ct)
    {
        var all = await QueryAsync(taskId, PageRequest.MaximumLimit, ct).ConfigureAwait(false);
        var offset = request.NormalizedOffset;
        return new Page<BackupRunRecord>(all.Skip(offset).Take(request.NormalizedLimit).ToArray(), offset, request.NormalizedLimit, all.Count);
    }

    private async Task EnsureMigratedAsync(CancellationToken ct)
    {
        await _migrationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _database.InitializeAsync(ct).ConfigureAwait(false);
            if (!File.Exists(_legacyHistoryPath)) return;
            await using var stream = File.OpenRead(_legacyHistoryPath);
            var legacy = await JsonSerializer.DeserializeAsync<List<LegacyBackupRunRecord>>(stream, cancellationToken: ct).ConfigureAwait(false) ?? [];
            foreach (var old in legacy)
            {
                await AppendMigratedAsync(old, ct).ConfigureAwait(false);
            }
            File.Move(_legacyHistoryPath, _legacyHistoryPath + ".migrated", overwrite: true);
        }
        finally { _migrationGate.Release(); }
    }

    private async Task AppendMigratedAsync(LegacyBackupRunRecord old, CancellationToken ct)
    {
        var state = old.Success ? BackupRunState.Succeeded : BackupRunState.Failed;
        var record = new BackupRunRecord
        {
            RunId = old.RunId, TaskId = old.TaskId, Operation = old.Operation, State = state,
            StartedAt = old.StartedAt, FinishedAt = old.FinishedAt, Success = old.Success, ExitCode = old.ExitCode,
            Stdout = old.Stdout, Stderr = old.Stderr, Report = new BackupRunReport { AttemptCount = 1, ErrorCode = old.Success ? null : "LEGACY_BACKUP_FAILED" }
        };
        await using var connection = await _database.GetConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO backup_runs(run_id, task_id, state, started_at, finished_at, report_json) VALUES($id,$task,$state,$started,$finished,$report);";
        command.Parameters.AddWithValue("$id", record.RunId);
        command.Parameters.AddWithValue("$task", record.TaskId);
        command.Parameters.AddWithValue("$state", state.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("$started", record.StartedAt.ToString("O"));
        command.Parameters.AddWithValue("$finished", record.FinishedAt?.ToString("O") ?? DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$report", JsonSerializer.Serialize(record));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private sealed record LegacyBackupRunRecord(string RunId, string TaskId, string Operation, DateTimeOffset StartedAt, DateTimeOffset FinishedAt, bool Success, int ExitCode, string Stdout, string Stderr);
}

public enum BackupRunState { Queued, Running, Succeeded, Failed, RolledBack }

/// <summary>Stable, auditable run report.</summary>
public sealed record BackupRunReport
{
    public int AttemptCount { get; init; }
    public string? ErrorCode { get; init; }
    public string? ChecksumManifestPath { get; init; }
    public bool ChecksumVerified { get; init; }
    public string? CheckpointPath { get; init; }
    public long? BytesProcessed { get; init; }
}

/// <summary>Backup run record.</summary>
public sealed record BackupRunRecord
{
    public required string RunId { get; init; }
    public required string TaskId { get; init; }
    public required string Operation { get; init; }
    public BackupRunState State { get; init; } = BackupRunState.Queued;
    public required DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Stdout { get; init; } = string.Empty;
    public string Stderr { get; init; } = string.Empty;
    public long? LeaseToken { get; init; }
    public BackupRunReport Report { get; init; } = new();
}
