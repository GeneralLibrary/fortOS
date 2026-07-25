using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace GNAS.Core;

/// <summary>SQLite provider with transactional, versioned schema migrations.</summary>
public sealed class DatabaseProvider : IDatabaseProvider
{
    private const string DefaultDataRoot = "/srv/nas";
    private static readonly SemaphoreSlim InitializationLock = new(1, 1);
    private readonly string _dataRoot;
    private readonly string _databaseDirectory;
    private readonly string _databasePath;

    public DatabaseProvider(string? dataRoot = null)
    {
        Batteries_V2.Init();
        var root = string.IsNullOrWhiteSpace(dataRoot) ? Environment.GetEnvironmentVariable("GNAS_DATA_ROOT") : dataRoot;
        _dataRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(root) ? DefaultDataRoot : root);
        _databaseDirectory = Path.GetFullPath(Path.Combine(_dataRoot, "database"));
        _databasePath = Path.GetFullPath(Path.Combine(_databaseDirectory, "nas.db"));
        EnsureDatabasePathIsSafe();
        ConnectionString = new SqliteConnectionStringBuilder { DataSource = _databasePath, DefaultTimeout = 30 }.ToString();
    }

    public string ConnectionString { get; }

    public async Task<SqliteConnection> GetConnectionAsync(CancellationToken ct)
    {
        EnsureDatabasePathIsSafe();
        Directory.CreateDirectory(_databaseDirectory);
        var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;", ct).ConfigureAwait(false);
        return connection;
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        await InitializationLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var connection = await GetConnectionAsync(ct).ConfigureAwait(false);
            await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;", ct).ConfigureAwait(false);
            await ExecuteAsync(connection, "CREATE TABLE IF NOT EXISTS schema_migrations (version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL);", ct).ConfigureAwait(false);
            foreach (var migration in Migrations)
            {
                await using var check = connection.CreateCommand();
                check.CommandText = "SELECT 1 FROM schema_migrations WHERE version = $version;";
                check.Parameters.AddWithValue("$version", migration.Version);
                if (await check.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null) continue;

                await using var transaction = connection.BeginTransaction();
                try
                {
                    await using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = migration.Sql;
                    await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    command.CommandText = "INSERT INTO schema_migrations(version, applied_at) VALUES($version, $applied_at);";
                    command.Parameters.Clear();
                    command.Parameters.AddWithValue("$version", migration.Version);
                    command.Parameters.AddWithValue("$applied_at", DateTimeOffset.UtcNow.ToString("O"));
                    await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    await transaction.CommitAsync(ct).ConfigureAwait(false);
                }
                catch
                {
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
            }
        }
        finally { InitializationLock.Release(); }
    }

    private void EnsureDatabasePathIsSafe()
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var rootWithSeparator = _dataRoot.EndsWith(Path.DirectorySeparatorChar) ? _dataRoot : _dataRoot + Path.DirectorySeparatorChar;
        if (!_databaseDirectory.StartsWith(rootWithSeparator, comparison) && !string.Equals(_databaseDirectory, _dataRoot, comparison))
            throw new ConfigurationException("数据库目录必须位于数据根目录内。");
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static readonly Migration[] Migrations =
    [
        new(1, """
CREATE TABLE IF NOT EXISTS services (service_id TEXT PRIMARY KEY, display_name TEXT, service_type TEXT, startup TEXT, restart_policy TEXT, executable TEXT, compose_file TEXT, definition_json TEXT NOT NULL, created_at TEXT, updated_at TEXT);
CREATE TABLE IF NOT EXISTS service_dependencies (service_id TEXT NOT NULL, depends_on TEXT NOT NULL, PRIMARY KEY(service_id, depends_on), FOREIGN KEY(service_id) REFERENCES services(service_id) ON DELETE CASCADE, FOREIGN KEY(depends_on) REFERENCES services(service_id) ON DELETE CASCADE);
CREATE TABLE IF NOT EXISTS users (username TEXT PRIMARY KEY, password_hash TEXT NOT NULL, display_name TEXT, email TEXT, totp_secret TEXT, failed_attempts INT DEFAULT 0, locked_until TEXT, created_at TEXT NOT NULL, roles_json TEXT DEFAULT '[]');
CREATE TABLE IF NOT EXISTS roles (role_id TEXT PRIMARY KEY, name TEXT, capabilities_json TEXT DEFAULT '[]');
CREATE TABLE IF NOT EXISTS service_accounts (account_id TEXT PRIMARY KEY, api_key_hash TEXT NOT NULL, display_name TEXT, capabilities_json TEXT DEFAULT '[]', created_at TEXT);
CREATE TABLE IF NOT EXISTS token_revocations (jti TEXT PRIMARY KEY, revoked_at TEXT NOT NULL, reason TEXT);
CREATE TABLE IF NOT EXISTS audit_chain (sequence INTEGER PRIMARY KEY AUTOINCREMENT, log_id TEXT UNIQUE NOT NULL, timestamp TEXT NOT NULL, action TEXT, resource TEXT, user_id TEXT, granted INT, previous_hash TEXT, current_hash TEXT NOT NULL, chain_signature TEXT NOT NULL, entry_json TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS alert_rules (rule_id TEXT PRIMARY KEY, rule_json TEXT NOT NULL, enabled INT DEFAULT 1, updated_at TEXT);
CREATE TABLE IF NOT EXISTS metrics (id INTEGER PRIMARY KEY AUTOINCREMENT, metric_name TEXT NOT NULL, value REAL, unit TEXT, dimensions_json TEXT, timestamp TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS access_logs (id INTEGER PRIMARY KEY AUTOINCREMENT, timestamp TEXT NOT NULL, user_id TEXT, action TEXT, resource TEXT, client_ip TEXT, result TEXT, entry_json TEXT);
"""),
        new(2, """
CREATE TABLE IF NOT EXISTS resource_acls (resource_path TEXT NOT NULL, principal TEXT NOT NULL, capabilities_json TEXT NOT NULL DEFAULT '[]', PRIMARY KEY(resource_path, principal));
CREATE TABLE IF NOT EXISTS api_config (config_key TEXT PRIMARY KEY, value_ref TEXT NOT NULL, updated_at TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS idempotency_records (idempotency_key TEXT PRIMARY KEY, subject TEXT NOT NULL, method TEXT NOT NULL, path TEXT NOT NULL, status_code INTEGER NOT NULL, response_json TEXT NOT NULL, expires_at TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS leases (lease_name TEXT PRIMARY KEY, owner_id TEXT NOT NULL, fencing_token INTEGER NOT NULL, expires_at TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS backup_tasks (task_id TEXT PRIMARY KEY, task_json TEXT NOT NULL, updated_at TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS backup_runs (run_id TEXT PRIMARY KEY, task_id TEXT NOT NULL, state TEXT NOT NULL, started_at TEXT NOT NULL, finished_at TEXT, lease_token INTEGER, report_json TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS ix_idempotency_expires_at ON idempotency_records(expires_at);
CREATE INDEX IF NOT EXISTS ix_backup_runs_task_id ON backup_runs(task_id, started_at DESC);
CREATE INDEX IF NOT EXISTS ix_leases_expires_at ON leases(expires_at);
"""),
        new(3, """
ALTER TABLE idempotency_records ADD COLUMN request_hash TEXT NOT NULL DEFAULT '';
ALTER TABLE idempotency_records ADD COLUMN state TEXT NOT NULL DEFAULT 'completed';
ALTER TABLE idempotency_records ADD COLUMN updated_at TEXT;
CREATE INDEX IF NOT EXISTS ix_idempotency_state_expires ON idempotency_records(state, expires_at);
CREATE TABLE IF NOT EXISTS upload_sessions (
    session_id TEXT PRIMARY KEY,
    subject TEXT NOT NULL,
    target_path TEXT NOT NULL,
    temporary_path TEXT NOT NULL,
    expected_size INTEGER,
    expected_sha256 TEXT,
    received_bytes INTEGER NOT NULL DEFAULT 0,
    state TEXT NOT NULL,
    etag TEXT,
    expires_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_upload_sessions_subject_state ON upload_sessions(subject, state, expires_at);
""")
    ];

    private sealed record Migration(int Version, string Sql);
}
