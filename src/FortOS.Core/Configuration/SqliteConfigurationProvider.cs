using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace FortOS.Core.Configuration;

/// <summary>
/// Configuration provider backed by the api_config table: feeds runtime-written
/// configuration overrides into the IConfiguration read chain so that ConfigController.Put
/// writes take effect (previously the table was write-only, making the config API a "false success").
/// </summary>
public sealed class SqliteConfigurationSource : IConfigurationSource
{
    private readonly string _databasePath;

    /// <summary>Initializes the configuration source; when <paramref name="databasePath"/> is null, uses the same resolution logic as DatabaseProvider.</summary>
    public SqliteConfigurationSource(string? databasePath = null)
    {
        var root = Environment.GetEnvironmentVariable("FortOS_DATA_ROOT");
        var dataRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(root) ? "/srv/nas" : root);
        _databasePath = databasePath ?? Path.GetFullPath(Path.Combine(dataRoot, "database", "nas.db"));
    }

    /// <summary>Parameterless constructor (for use by <see cref="ConfigurationExtensions.Add{TSource}"/>).</summary>
    public SqliteConfigurationSource()
        : this(databasePath: null)
    {
    }

    /// <inheritdoc />
    public IConfigurationProvider Build(IConfigurationBuilder builder) => new SqliteConfigurationProvider(_databasePath);
}

/// <summary>Configuration provider that reads override values from the api_config table.</summary>
public sealed class SqliteConfigurationProvider : ConfigurationProvider
{
    private readonly string _databasePath;

    /// <summary>Initializes the provider.</summary>
    public SqliteConfigurationProvider(string databasePath) => _databasePath = databasePath;

    /// <inheritdoc />
    public override void Load()
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(_databasePath))
            {
                return;
            }

            using var connection = new SqliteConnection($"Data Source={_databasePath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT config_key, value_ref FROM api_config;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                data[reader.GetString(0)] = reader.GetString(1);
            }
        }
        catch (SqliteException)
        {
            // Do not block configuration startup when the database is uninitialized or unreadable (e.g. corrupt); keep the override set empty.
            // A subsequent Put write will rebuild the table and trigger Reload.
            data.Clear();
        }

        Data = data;
    }
}
