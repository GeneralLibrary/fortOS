using System.Text.Json;
using GNAS.Core;

namespace GNAS.Modules.Backup.Services;

/// <summary>备份任务 SQLite 仓库。</summary>
public sealed class BackupTaskStore
{
    private readonly IDatabaseProvider _database;
    private readonly string _legacyPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public BackupTaskStore(IDatabaseProvider database)
    {
        _database = database;
        var root = Environment.GetEnvironmentVariable("GNAS_DATA_ROOT");
        _legacyPath = Path.Combine(string.IsNullOrWhiteSpace(root) ? "/srv/nas" : root, "modules", "loaded", "backup", "config", "backup-tasks.json");
    }

    public async Task<IReadOnlyList<BackupTask>> ListAsync(CancellationToken ct)
    {
        await EnsureMigratedAsync(ct).ConfigureAwait(false);
        await using var connection = await _database.GetConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT task_json FROM backup_tasks ORDER BY task_id;";
        var result = new List<BackupTask>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var task = JsonSerializer.Deserialize<BackupTask>(reader.GetString(0));
            if (task is not null) result.Add(task);
        }
        return result;
    }

    public async Task UpsertAsync(BackupTask task, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(task);
        await EnsureMigratedAsync(ct).ConfigureAwait(false);
        await using var connection = await _database.GetConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
INSERT INTO backup_tasks(task_id, task_json, updated_at) VALUES($id, $json, $updated)
ON CONFLICT(task_id) DO UPDATE SET task_json = excluded.task_json, updated_at = excluded.updated_at;
""";
        command.Parameters.AddWithValue("$id", task.TaskId);
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(task));
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task ReplaceAllAsync(IEnumerable<BackupTask> tasks, CancellationToken ct)
    {
        await EnsureMigratedAsync(ct).ConfigureAwait(false);
        await using var connection = await _database.GetConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction();
        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM backup_tasks;";
            await clear.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        foreach (var task in tasks)
        {
            await using var write = connection.CreateCommand();
            write.Transaction = transaction;
            write.CommandText = "INSERT INTO backup_tasks(task_id, task_json, updated_at) VALUES($id,$json,$updated);";
            write.Parameters.AddWithValue("$id", task.TaskId);
            write.Parameters.AddWithValue("$json", JsonSerializer.Serialize(task));
            write.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
            await write.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(string taskId, CancellationToken ct)
    {
        await EnsureMigratedAsync(ct).ConfigureAwait(false);
        await using var connection = await _database.GetConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM backup_tasks WHERE task_id = $id;";
        command.Parameters.AddWithValue("$id", taskId);
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
    }

    private async Task EnsureMigratedAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _database.InitializeAsync(ct).ConfigureAwait(false);
            if (!File.Exists(_legacyPath)) return;
            await using var stream = File.OpenRead(_legacyPath);
            var tasks = await JsonSerializer.DeserializeAsync<List<BackupTask>>(stream, cancellationToken: ct).ConfigureAwait(false) ?? [];
            foreach (var task in tasks)
            {
                await UpsertWithoutMigrationAsync(task, ct).ConfigureAwait(false);
            }
            File.Move(_legacyPath, _legacyPath + ".migrated", overwrite: true);
        }
        finally { _gate.Release(); }
    }

    private async Task UpsertWithoutMigrationAsync(BackupTask task, CancellationToken ct)
    {
        await using var connection = await _database.GetConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR REPLACE INTO backup_tasks(task_id, task_json, updated_at) VALUES($id,$json,$updated);";
        command.Parameters.AddWithValue("$id", task.TaskId);
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(task));
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
