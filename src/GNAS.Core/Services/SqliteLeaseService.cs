using Microsoft.Data.Sqlite;

namespace GNAS.Core;

/// <summary>Distributed lease with fencing token, backed by SQLite transactions.</summary>
public sealed class SqliteLeaseService
{
    private readonly IDatabaseProvider _database;

    public SqliteLeaseService(IDatabaseProvider database) => _database = database;

    public async Task<LeaseHandle?> AcquireAsync(string name, string owner, TimeSpan ttl, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (ttl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ttl));

        await _database.InitializeAsync(ct).ConfigureAwait(false);
        await using var connection = await _database.GetConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction();
        var now = DateTimeOffset.UtcNow;
        var expires = now.Add(ttl);
        long token;

        await using (var query = connection.CreateCommand())
        {
            query.Transaction = transaction;
            query.CommandText = "SELECT owner_id, fencing_token, expires_at FROM leases WHERE lease_name = $name;";
            query.Parameters.AddWithValue("$name", name);
            await using var reader = await query.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var currentOwner = reader.GetString(0);
                var currentToken = reader.GetInt64(1);
                var currentExpiry = DateTimeOffset.Parse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind);
                if (!string.Equals(currentOwner, owner, StringComparison.Ordinal) && currentExpiry > now)
                {
                    await transaction.RollbackAsync(ct).ConfigureAwait(false);
                    return null;
                }

                token = currentToken + (string.Equals(currentOwner, owner, StringComparison.Ordinal) ? 0 : 1);
            }
            else
            {
                token = 1;
            }
        }

        await using (var write = connection.CreateCommand())
        {
            write.Transaction = transaction;
            write.CommandText = """
INSERT INTO leases(lease_name, owner_id, fencing_token, expires_at) VALUES($name, $owner, $token, $expires)
ON CONFLICT(lease_name) DO UPDATE SET owner_id = excluded.owner_id, fencing_token = excluded.fencing_token, expires_at = excluded.expires_at;
""";
            write.Parameters.AddWithValue("$name", name);
            write.Parameters.AddWithValue("$owner", owner);
            write.Parameters.AddWithValue("$token", token);
            write.Parameters.AddWithValue("$expires", expires.ToString("O"));
            await write.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return new LeaseHandle(name, owner, token, expires);
    }

    public async Task<bool> RenewAsync(LeaseHandle lease, TimeSpan ttl, CancellationToken ct)
    {
        if (ttl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ttl));
        await _database.InitializeAsync(ct).ConfigureAwait(false);
        await using var connection = await _database.GetConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var expires = DateTimeOffset.UtcNow.Add(ttl);
        command.CommandText = "UPDATE leases SET expires_at = $expires WHERE lease_name = $name AND owner_id = $owner AND fencing_token = $token AND expires_at > $now;";
        command.Parameters.AddWithValue("$expires", expires.ToString("O"));
        command.Parameters.AddWithValue("$name", lease.Name);
        command.Parameters.AddWithValue("$owner", lease.Owner);
        command.Parameters.AddWithValue("$token", lease.FencingToken);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
    }

    public async Task<bool> ReleaseAsync(LeaseHandle lease, CancellationToken ct)
    {
        await _database.InitializeAsync(ct).ConfigureAwait(false);
        await using var connection = await _database.GetConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM leases WHERE lease_name = $name AND owner_id = $owner AND fencing_token = $token;";
        command.Parameters.AddWithValue("$name", lease.Name);
        command.Parameters.AddWithValue("$owner", lease.Owner);
        command.Parameters.AddWithValue("$token", lease.FencingToken);
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
    }
}

/// <summary>Non-forgeable lease credential handle.</summary>
public sealed record LeaseHandle(string Name, string Owner, long FencingToken, DateTimeOffset ExpiresAt);
