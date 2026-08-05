using FortOS.Api.Configuration;
using FortOS.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace FortOS.Api.Controllers;

/// <summary>Configuration controller.</summary>
[Route("api/config")]
public sealed class ConfigController : FortOSControllerBase
{
    /// <summary>Return non-sensitive flat configuration.</summary>
    [HttpGet]
    public object Get([FromServices] IConfiguration configuration) => configuration.AsEnumerable()
        .Where(p => p.Value is not null && !ConfigMetaRegistry.IsSensitive(p.Key))
        .ToDictionary(p => p.Key, p => p.Value);

    /// <summary>
    /// Return metadata describing whitelisted, user-editable configuration:
    /// semantic categories, control types, options and validation hints.
    /// The dashboard renders its settings UI from this shape.
    /// </summary>
    [HttpGet("meta")]
    public object Meta() => new
    {
        categories = ConfigMetaRegistry.Categories,
        entries = ConfigMetaRegistry.Entries.Select(e => new
        {
            e.Key,
            e.Category,
            type = e.TypeName,
            e.Label,
            e.Description,
            e.Options,
            e.Min,
            e.Max,
            e.Step,
            e.DefaultValue,
            e.Order,
        }),
    };

    /// <summary>Write runtime configuration override value.</summary>
    [HttpPut("{key}")]
    public async Task<object> Put(string key, [FromBody] ConfigValue value, [FromServices] IDatabaseProvider database, [FromServices] IConfiguration configuration, CancellationToken ct)
    {
        if (ConfigMetaRegistry.IsSensitive(key)) throw new ArgumentException("Writing sensitive configuration through this endpoint is prohibited.", nameof(key));
        await database.InitializeAsync(ct).ConfigureAwait(false);
        await using var connection = await database.GetConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        // Column names must match the schema created by DatabaseProvider migration 2
        // (config_key / value_ref / updated_at); the previous mismatch surfaced as 500.
        command.CommandText = "CREATE TABLE IF NOT EXISTS api_config(config_key TEXT PRIMARY KEY, value_ref TEXT NOT NULL, updated_at TEXT NOT NULL); INSERT OR REPLACE INTO api_config(config_key, value_ref, updated_at) VALUES($key, $value, $updated);";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value.Value ?? string.Empty);
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        // 写入已落库，再触发配置链重载：SqliteConfigurationProvider 重新读取
        // api_config 表，覆盖值立即对 IConfiguration 读取方（如 metrics:*、
        // rateLimit:*、idempotency:*）生效，而不是只写不读的「假成功」。
        if (configuration is IConfigurationRoot root)
        {
            root.Reload();
        }

        return new { success = true, key };
    }
}
