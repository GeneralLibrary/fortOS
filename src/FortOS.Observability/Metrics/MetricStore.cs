using System.Globalization;
using System.Text.Json;
using FortOS.Core;

namespace FortOS.Observability.Metrics;

/// <summary>Persists scalar metrics in SQLite and applies bounded retention.</summary>
public sealed class MetricStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDatabaseProvider _database;

    /// <summary>Initialize the metric store.</summary>
    public MetricStore(IDatabaseProvider database) => _database = database;

    /// <summary>Append one collection batch atomically.</summary>
    public async Task AppendAsync(IEnumerable<MetricData> metrics, CancellationToken ct)
    {
        await _database.InitializeAsync(ct).ConfigureAwait(false);
        await using var connection = await _database.GetConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO metrics(metric_name, value, unit, dimensions_json, timestamp) VALUES($name, $value, $unit, $dimensions, $timestamp);";
        var name = command.Parameters.Add("$name", Microsoft.Data.Sqlite.SqliteType.Text);
        var value = command.Parameters.Add("$value", Microsoft.Data.Sqlite.SqliteType.Real);
        var unit = command.Parameters.Add("$unit", Microsoft.Data.Sqlite.SqliteType.Text);
        var dimensions = command.Parameters.Add("$dimensions", Microsoft.Data.Sqlite.SqliteType.Text);
        var timestamp = command.Parameters.Add("$timestamp", Microsoft.Data.Sqlite.SqliteType.Text);
        foreach (var metric in metrics)
        {
            name.Value = metric.MetricName;
            value.Value = metric.Value;
            unit.Value = metric.Unit;
            dimensions.Value = JsonSerializer.Serialize(metric.Dimensions, JsonOptions);
            timestamp.Value = metric.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Query newest metric records first.</summary>
    public async Task<IReadOnlyList<MetricData>> QueryAsync(SystemMetricHistoryQuery query, CancellationToken ct)
    {
        await _database.InitializeAsync(ct).ConfigureAwait(false);
        await using var connection = await _database.GetConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT metric_name, value, unit, dimensions_json, timestamp
            FROM metrics
            WHERE ($name IS NULL OR metric_name = $name)
              AND ($from IS NULL OR timestamp >= $from)
            ORDER BY timestamp DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$name", (object?)query.MetricName ?? DBNull.Value);
        command.Parameters.AddWithValue("$from", query.From?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$limit", Math.Clamp(query.Limit, 1, 5000));
        var result = new List<MetricData>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            result.Add(new MetricData
            {
                MetricName = reader.GetString(0),
                Value = reader.GetDouble(1),
                Unit = reader.GetString(2),
                Dimensions = reader.IsDBNull(3)
                    ? []
                    : JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(3), JsonOptions) ?? [],
                Timestamp = DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            });
        }
        return result;
    }

    /// <summary>Delete records older than the configured retention boundary.</summary>
    public async Task PruneAsync(DateTimeOffset olderThan, CancellationToken ct)
    {
        await _database.InitializeAsync(ct).ConfigureAwait(false);
        await using var connection = await _database.GetConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM metrics WHERE timestamp < $cutoff;";
        command.Parameters.AddWithValue("$cutoff", olderThan.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
