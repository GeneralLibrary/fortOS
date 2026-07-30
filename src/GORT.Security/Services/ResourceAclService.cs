using System.Text.Json;
using GORT.Core;

namespace GORT.Security.Services;

/// <summary>SQLite-backed ACL store. A path ACL replaces inherited ACLs for that subtree.</summary>
public sealed class ResourceAclService : IResourceAclService
{
    private readonly IDatabaseProvider _database;
    public ResourceAclService(IDatabaseProvider database) => _database = database;

    public async Task SetAsync(string resourcePath, string principal, IEnumerable<string> capabilities, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(principal);
        var normalized = NormalizePath(resourcePath);
        var values = capabilities?.Distinct(StringComparer.Ordinal).ToArray() ?? [];
        await _database.InitializeAsync(ct).ConfigureAwait(false);
        await using var connection = await _database.GetConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO resource_acls(resource_path, principal, capabilities_json) VALUES($path,$principal,$capabilities) ON CONFLICT(resource_path,principal) DO UPDATE SET capabilities_json=excluded.capabilities_json;";
        command.Parameters.AddWithValue("$path", normalized);
        command.Parameters.AddWithValue("$principal", principal);
        command.Parameters.AddWithValue("$capabilities", JsonSerializer.Serialize(values));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string resourcePath, string principal, CancellationToken ct)
    {
        await _database.InitializeAsync(ct).ConfigureAwait(false);
        await using var connection = await _database.GetConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM resource_acls WHERE resource_path=$path AND principal=$principal;";
        command.Parameters.AddWithValue("$path", NormalizePath(resourcePath));
        command.Parameters.AddWithValue("$principal", principal);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetAsync(string resourcePath, CancellationToken ct)
    {
        await _database.InitializeAsync(ct).ConfigureAwait(false);
        await using var connection = await _database.GetConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT principal, capabilities_json FROM resource_acls WHERE resource_path=$path;";
        command.Parameters.AddWithValue("$path", NormalizePath(resourcePath));
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            result[reader.GetString(0)] = JsonSerializer.Deserialize<string[]>(reader.GetString(1)) ?? [];
        return result;
    }

    internal static string NormalizePath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).Replace('\\', '/');
}
