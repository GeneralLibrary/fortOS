using System.Collections.Concurrent;
using System.Text.Json;
using FortOS.Core;
using Microsoft.Data.Sqlite;

namespace FortOS.ServiceBus.Registry;

/// <summary>
/// SQLite-based service registry.
/// </summary>
public sealed class ServiceRegistry : IServiceRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDatabaseProvider _databaseProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ConcurrentDictionary<string, ServiceDefinition>? _cache;

    /// <summary>
    /// Initialize the service registry.
    /// </summary>
    /// <param name="databaseProvider">Database provider.</param>
    public ServiceRegistry(IDatabaseProvider databaseProvider)
    {
        _databaseProvider = databaseProvider;
    }

    /// <inheritdoc />
    public async Task RegisterAsync(ServiceDefinition definition, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(definition);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var cache = await EnsureLoadedNoLockAsync(ct).ConfigureAwait(false);
            var graph = BuildGraph(cache.Values.Where(s => s.ServiceId != definition.ServiceId).Append(definition));
            ThrowIfCircular(graph);
            await UpsertAsync(definition, ct).ConfigureAwait(false);
            cache[definition.ServiceId] = definition;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ServiceDefinition?> GetAsync(string serviceId, CancellationToken ct)
    {
        var cache = await EnsureLoadedNoLockAsync(ct).ConfigureAwait(false);
        if (cache.TryGetValue(serviceId, out var definition))
        {
            return definition;
        }
        return null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ServiceDefinition>> ListAsync(CancellationToken ct)
    {
        var cache = await EnsureLoadedNoLockAsync(ct).ConfigureAwait(false);
        return cache.Values.OrderBy(s => s.ServiceId, StringComparer.Ordinal).ToArray();
    }

    /// <inheritdoc />
    public async Task UpdateAsync(ServiceDefinition definition, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(definition);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var cache = await EnsureLoadedNoLockAsync(ct).ConfigureAwait(false);
            if (!cache.ContainsKey(definition.ServiceId))
            {
                throw new ServiceNotFoundException($"Service does not exist: {definition.ServiceId}");
            }

            var graph = BuildGraph(cache.Values.Where(s => s.ServiceId != definition.ServiceId).Append(definition));
            ThrowIfCircular(graph);
            await UpsertAsync(definition, ct).ConfigureAwait(false);
            cache[definition.ServiceId] = definition;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task UnregisterAsync(string serviceId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var cache = await EnsureLoadedNoLockAsync(ct).ConfigureAwait(false);
            if (!cache.ContainsKey(serviceId))
            {
                throw new ServiceNotFoundException($"Service does not exist: {serviceId}");
            }

            await using var connection = await _databaseProvider.GetConnectionAsync(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM services WHERE service_id = $service_id";
            command.Parameters.AddWithValue("$service_id", serviceId);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            cache.TryRemove(serviceId, out _);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ServiceDefinition>> GetDependentsAsync(string serviceId, CancellationToken ct)
    {
        var cache = await EnsureLoadedAsync(ct).ConfigureAwait(false);
        if (!cache.ContainsKey(serviceId))
        {
            throw new ServiceNotFoundException($"Service not found: {serviceId}");
        }

        return cache.Values.Where(s => s.DependsOn.Contains(serviceId, StringComparer.Ordinal)).OrderBy(s => s.ServiceId, StringComparer.Ordinal).ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ServiceDefinition>> GetDependenciesAsync(string serviceId, CancellationToken ct)
    {
        var cache = await EnsureLoadedAsync(ct).ConfigureAwait(false);
        if (!cache.TryGetValue(serviceId, out var service))
        {
            throw new ServiceNotFoundException($"Service not found: {serviceId}");
        }

        return service.DependsOn.Where(cache.ContainsKey).Select(id => cache[id]).OrderBy(s => s.ServiceId, StringComparer.Ordinal).ToArray();
    }

    private async Task<ConcurrentDictionary<string, ServiceDefinition>> EnsureLoadedAsync(CancellationToken ct)
    {
        if (_cache is not null)
        {
            return _cache;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cache is not null)
            {
                return _cache;
            }

            return await EnsureLoadedNoLockAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ConcurrentDictionary<string, ServiceDefinition>> EnsureLoadedNoLockAsync(CancellationToken ct)
    {
        if (_cache is not null)
        {
            return _cache;
        }

        await _databaseProvider.InitializeAsync(ct).ConfigureAwait(false);
        var loaded = new ConcurrentDictionary<string, ServiceDefinition>(StringComparer.Ordinal);
        await using var connection = await _databaseProvider.GetConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT definition_json FROM services ORDER BY service_id";
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var json = reader.GetString(0);
            var definition = JsonSerializer.Deserialize<ServiceDefinition>(json, JsonOptions)
                ?? throw new InvalidOperationException("Service definition JSON is empty.");
            loaded[definition.ServiceId] = definition;
        }

        _cache = loaded;
        return loaded;
    }

    private async Task UpsertAsync(ServiceDefinition definition, CancellationToken ct)
    {
        await _databaseProvider.InitializeAsync(ct).ConfigureAwait(false);
        await using var connection = await _databaseProvider.GetConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow.ToString("O");
        var json = JsonSerializer.Serialize(definition, JsonOptions);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
INSERT INTO services(service_id, display_name, service_type, startup, restart_policy, executable, compose_file, definition_json, created_at, updated_at)
VALUES($service_id, $display_name, $service_type, $startup, $restart_policy, $executable, $compose_file, $definition_json, $now, $now)
ON CONFLICT(service_id) DO UPDATE SET
    display_name = excluded.display_name,
    service_type = excluded.service_type,
    startup = excluded.startup,
    restart_policy = excluded.restart_policy,
    executable = excluded.executable,
    compose_file = excluded.compose_file,
    definition_json = excluded.definition_json,
    updated_at = excluded.updated_at
""";
            command.Parameters.AddWithValue("$service_id", definition.ServiceId);
            command.Parameters.AddWithValue("$display_name", definition.DisplayName);
            command.Parameters.AddWithValue("$service_type", definition.Type.ToString());
            command.Parameters.AddWithValue("$startup", definition.Startup.ToString());
            command.Parameters.AddWithValue("$restart_policy", definition.RestartPolicy.ToString());
            command.Parameters.AddWithValue("$executable", (object?)definition.Executable ?? DBNull.Value);
            command.Parameters.AddWithValue("$compose_file", (object?)definition.ComposeFile ?? DBNull.Value);
            command.Parameters.AddWithValue("$definition_json", json);
            command.Parameters.AddWithValue("$now", now);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = (SqliteTransaction)transaction;
            delete.CommandText = "DELETE FROM service_dependencies WHERE service_id = $service_id";
            delete.Parameters.AddWithValue("$service_id", definition.ServiceId);
            await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var dependency in definition.DependsOn.Distinct(StringComparer.Ordinal))
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = (SqliteTransaction)transaction;
            insert.CommandText = "INSERT INTO service_dependencies(service_id, depends_on) VALUES($service_id, $depends_on)";
            insert.Parameters.AddWithValue("$service_id", definition.ServiceId);
            insert.Parameters.AddWithValue("$depends_on", dependency);
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    private static Dictionary<string, string[]> BuildGraph(IEnumerable<ServiceDefinition> definitions)
        => definitions.ToDictionary(s => s.ServiceId, s => s.DependsOn.Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);

    private static void ThrowIfCircular(IReadOnlyDictionary<string, string[]> graph)
    {
        var states = new Dictionary<string, int>(StringComparer.Ordinal);
        var stack = new List<string>();

        foreach (var node in graph.Keys)
        {
            if (Visit(node, graph, states, stack, out var cycle))
            {
                throw new CircularDependencyException($"Service dependencies contain a cycle: {string.Join(" -> ", cycle)}");
            }
        }
    }

    private static bool Visit(string node, IReadOnlyDictionary<string, string[]> graph, Dictionary<string, int> states, List<string> stack, out IReadOnlyList<string> cycle)
    {
        cycle = Array.Empty<string>();
        if (states.TryGetValue(node, out var state))
        {
            if (state == 1)
            {
                var index = stack.IndexOf(node);
                cycle = stack.Skip(index).Append(node).ToArray();
                return true;
            }

            return false;
        }

        states[node] = 1;
        stack.Add(node);
        if (graph.TryGetValue(node, out var dependencies))
        {
            foreach (var dependency in dependencies)
            {
                if (graph.ContainsKey(dependency) && Visit(dependency, graph, states, stack, out cycle))
                {
                    return true;
                }
            }
        }

        stack.RemoveAt(stack.Count - 1);
        states[node] = 2;
        return false;
    }
}
