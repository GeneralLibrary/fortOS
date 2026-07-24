using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace GNAS.Core;

/// <summary>
/// 基于 Microsoft.Data.Sqlite 的 GNAS 数据库提供器。
/// </summary>
public sealed class DatabaseProvider : IDatabaseProvider
{
    private const string DefaultDataRoot = "/srv/nas";
    private readonly string _dataRoot;
    private readonly string _databaseDirectory;
    private readonly string _databasePath;

    /// <summary>
    /// 初始化数据库提供器。
    /// </summary>
    /// <param name="dataRoot">数据根目录，为空时读取 GNAS_DATA_ROOT 或使用默认路径。</param>
    public DatabaseProvider(string? dataRoot = null)
    {
        Batteries_V2.Init();
        var root = string.IsNullOrWhiteSpace(dataRoot)
            ? Environment.GetEnvironmentVariable("GNAS_DATA_ROOT")
            : dataRoot;
        root = string.IsNullOrWhiteSpace(root) ? DefaultDataRoot : root;
        _dataRoot = Path.GetFullPath(root);
        _databaseDirectory = Path.GetFullPath(Path.Combine(_dataRoot, "database"));
        _databasePath = Path.GetFullPath(Path.Combine(_databaseDirectory, "nas.db"));
        EnsureDatabasePathIsSafe();
        ConnectionString = new SqliteConnectionStringBuilder { DataSource = _databasePath }.ToString();
    }

    /// <inheritdoc />
    public string ConnectionString { get; }

    /// <inheritdoc />
    public async Task<SqliteConnection> GetConnectionAsync(CancellationToken ct)
    {
        EnsureDatabasePathIsSafe();
        Directory.CreateDirectory(_databaseDirectory);
        var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        await pragma.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return connection;
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken ct)
    {
        await using var connection = await GetConnectionAsync(ct).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;", ct).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;", ct).ConfigureAwait(false);
        await ExecuteAsync(connection, SchemaSql, ct).ConfigureAwait(false);
    }

    private void EnsureDatabasePathIsSafe()
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var rootWithSeparator = _dataRoot.EndsWith(Path.DirectorySeparatorChar) ? _dataRoot : _dataRoot + Path.DirectorySeparatorChar;
        if (!_databaseDirectory.StartsWith(rootWithSeparator, comparison) && !string.Equals(_databaseDirectory, _dataRoot, comparison))
        {
            throw new ConfigurationException("数据库目录必须位于数据根目录内。");
        }
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private const string SchemaSql = """
CREATE TABLE IF NOT EXISTS services (
    service_id TEXT PRIMARY KEY,
    display_name TEXT,
    service_type TEXT,
    startup TEXT,
    restart_policy TEXT,
    executable TEXT,
    compose_file TEXT,
    definition_json TEXT NOT NULL,
    created_at TEXT,
    updated_at TEXT
);

CREATE TABLE IF NOT EXISTS service_dependencies (
    service_id TEXT NOT NULL,
    depends_on TEXT NOT NULL,
    PRIMARY KEY(service_id, depends_on),
    FOREIGN KEY(service_id) REFERENCES services(service_id) ON DELETE CASCADE,
    FOREIGN KEY(depends_on) REFERENCES services(service_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS users (
    username TEXT PRIMARY KEY,
    password_hash TEXT NOT NULL,
    display_name TEXT,
    email TEXT,
    totp_secret TEXT,
    failed_attempts INT DEFAULT 0,
    locked_until TEXT,
    created_at TEXT NOT NULL,
    roles_json TEXT DEFAULT '[]'
);

CREATE TABLE IF NOT EXISTS roles (
    role_id TEXT PRIMARY KEY,
    name TEXT,
    capabilities_json TEXT DEFAULT '[]'
);

CREATE TABLE IF NOT EXISTS service_accounts (
    account_id TEXT PRIMARY KEY,
    api_key_hash TEXT NOT NULL,
    display_name TEXT,
    capabilities_json TEXT DEFAULT '[]',
    created_at TEXT
);

CREATE TABLE IF NOT EXISTS token_revocations (
    jti TEXT PRIMARY KEY,
    revoked_at TEXT NOT NULL,
    reason TEXT
);

CREATE TABLE IF NOT EXISTS audit_chain (
    sequence INTEGER PRIMARY KEY AUTOINCREMENT,
    log_id TEXT UNIQUE NOT NULL,
    timestamp TEXT NOT NULL,
    action TEXT,
    resource TEXT,
    user_id TEXT,
    granted INT,
    previous_hash TEXT,
    current_hash TEXT NOT NULL,
    chain_signature TEXT NOT NULL,
    entry_json TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS alert_rules (
    rule_id TEXT PRIMARY KEY,
    rule_json TEXT NOT NULL,
    enabled INT DEFAULT 1,
    updated_at TEXT
);

CREATE TABLE IF NOT EXISTS metrics (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    metric_name TEXT NOT NULL,
    value REAL,
    unit TEXT,
    dimensions_json TEXT,
    timestamp TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS access_logs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    timestamp TEXT NOT NULL,
    user_id TEXT,
    action TEXT,
    resource TEXT,
    client_ip TEXT,
    result TEXT,
    entry_json TEXT
);
""";
}
