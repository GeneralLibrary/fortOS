using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GNAS.Core;
using Microsoft.Data.Sqlite;

namespace GNAS.Observability.Audit;

/// <summary>Non-tamperable audit chain based on SQLite and HMAC.</summary>
public sealed class AuditChain : IAuditChain
{
    private const string GenesisHash = "GENESIS";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDatabaseProvider _database;
    private readonly INasKeyStore _keyStore;
    private readonly SemaphoreSlim _appendLock = new(1, 1);
    private bool _loaded;
    private string _lastHash = GenesisHash;

    /// <summary>Initialize audit chain.</summary>
    public AuditChain(IDatabaseProvider database, INasKeyStore keyStore)
    {
        _database = database;
        _keyStore = keyStore;
    }

    /// <inheritdoc />
    public async Task AppendAsync(LogEntry entry, CancellationToken ct)
    {
        if (entry.Audit is null) return;
        await _appendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);
            var previous = _lastHash;
            var current = ComputeHash(previous, entry.Timestamp, entry.Audit.Action, entry.Audit.Resource, entry.UserId, entry.Audit.Granted, entry.Audit.AfterState);
            var signature = await ComputeSignatureAsync(current, ct).ConfigureAwait(false);
            var chainedAudit = entry.Audit with { PreviousHash = previous, CurrentHash = current, ChainSignature = signature };
            var chainedEntry = entry with { Audit = chainedAudit, Category = LogCategory.Audit };
            var json = JsonSerializer.Serialize(chainedEntry, JsonOptions);

            await using var connection = await _database.GetConnectionAsync(ct).ConfigureAwait(false);
            await EnsureTableAsync(connection, ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
INSERT INTO audit_chain (log_id, timestamp, action, resource, user_id, granted, previous_hash, current_hash, chain_signature, entry_json)
VALUES ($log_id, $timestamp, $action, $resource, $user_id, $granted, $previous_hash, $current_hash, $chain_signature, $entry_json);
""";
            Add(command, "$log_id", chainedEntry.LogId);
            Add(command, "$timestamp", chainedEntry.Timestamp.ToString("O"));
            Add(command, "$action", chainedAudit.Action);
            Add(command, "$resource", chainedAudit.Resource);
            Add(command, "$user_id", chainedEntry.UserId);
            Add(command, "$granted", chainedAudit.Granted ? 1 : 0);
            Add(command, "$previous_hash", chainedAudit.PreviousHash);
            Add(command, "$current_hash", chainedAudit.CurrentHash);
            Add(command, "$chain_signature", chainedAudit.ChainSignature);
            Add(command, "$entry_json", json);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            _lastHash = current;
        }
        finally
        {
            _appendLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ChainVerificationResult> VerifyIntegrityAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        await using var connection = await _database.GetConnectionAsync(ct).ConfigureAwait(false);
        await EnsureTableAsync(connection, ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var filters = new List<string>();
        if (from is not null) filters.Add("timestamp >= $from");
        if (to is not null) filters.Add("timestamp <= $to");
        command.CommandText = "SELECT sequence, timestamp, previous_hash, current_hash, chain_signature, entry_json FROM audit_chain" +
                              (filters.Count > 0 ? " WHERE " + string.Join(" AND ", filters) : string.Empty) +
                              " ORDER BY sequence ASC;";
        if (from is not null) Add(command, "$from", from.Value.ToString("O"));
        if (to is not null) Add(command, "$to", to.Value.ToString("O"));

        long total = 0;
        string? expectedPrevious = null;
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            total++;
            var sequence = reader.GetInt64(0);
            var previousHash = reader.GetString(2);
            var currentHash = reader.GetString(3);
            var signature = reader.GetString(4);
            var json = reader.GetString(5);
            var entry = JsonSerializer.Deserialize<LogEntry>(json, JsonOptions);
            if (entry?.Audit is null)
            {
                return Broken(sequence, total, "Invalid audit entry JSON.");
            }

            if (expectedPrevious is not null && previousHash != expectedPrevious)
            {
                return Broken(sequence, total, "Audit chain previous hash discontinuity.");
            }

            var recomputed = ComputeHash(previousHash, entry.Timestamp, entry.Audit.Action, entry.Audit.Resource, entry.UserId, entry.Audit.Granted, entry.Audit.AfterState);
            var recomputedSignature = await ComputeSignatureAsync(recomputed, ct).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(currentHash), Encoding.UTF8.GetBytes(recomputed)) ||
                !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(signature), Encoding.UTF8.GetBytes(recomputedSignature)))
            {
                return Broken(sequence, total, "Audit chain hash or signature mismatch.");
            }

            expectedPrevious = currentHash;
        }

        return new ChainVerificationResult { IsValid = true, TotalEntries = total, Message = "Audit chain is intact." };
    }

    /// <inheritdoc />
    public async Task ExportAsync(DateOnly date, string path, CancellationToken ct)
    {
        var from = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local);
        var to = date.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Local);
        await using var connection = await _database.GetConnectionAsync(ct).ConfigureAwait(false);
        await EnsureTableAsync(connection, ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT entry_json FROM audit_chain WHERE timestamp >= $from AND timestamp <= $to ORDER BY sequence ASC;";
        Add(command, "$from", new DateTimeOffset(from).ToString("O"));
        Add(command, "$to", new DateTimeOffset(to).ToString("O"));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
        await using var output = File.CreateText(path);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            await output.WriteLineAsync(reader.GetString(0)).ConfigureAwait(false);
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_loaded) return;
        await _database.InitializeAsync(ct).ConfigureAwait(false);
        await using var connection = await _database.GetConnectionAsync(ct).ConfigureAwait(false);
        await EnsureTableAsync(connection, ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT current_hash FROM audit_chain ORDER BY sequence DESC LIMIT 1;";
        var value = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        _lastHash = value as string ?? GenesisHash;
        _loaded = true;
    }

    private static async Task EnsureTableAsync(SqliteConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
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
""";
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<string> ComputeSignatureAsync(string currentHash, CancellationToken ct)
    {
        var key = await _keyStore.GetOrCreateChainKeyAsync(ct).ConfigureAwait(false);
        using var hmac = new HMACSHA256(key);
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(currentHash))).ToLowerInvariant();
    }

    private static string ComputeHash(string previousHash, DateTimeOffset timestamp, string action, string resource, string? userId, bool granted, string? afterState)
    {
        var material = string.Concat(previousHash, timestamp.ToString("O"), action, resource, userId, granted, afterState);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }

    private static void Add(SqliteCommand command, string name, object? value) => command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static ChainVerificationResult Broken(long sequence, long total, string message) => new()
    {
        IsValid = false,
        TotalEntries = total,
        BrokenAtSequence = sequence,
        Message = message
    };
}
