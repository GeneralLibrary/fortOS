using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace FortOS.Core.Configuration;

/// <summary>
/// 以 api_config 表为数据源的配置提供者：把运行时写入的配置覆盖项接入
/// IConfiguration 读取链，使 ConfigController.Put 的写入真正生效
/// （此前该表只写不读，配置 API 是「假成功」）。
/// </summary>
public sealed class SqliteConfigurationSource : IConfigurationSource
{
    private readonly string _databasePath;

    /// <summary>初始化配置源；<paramref name="databasePath"/> 为空时与 DatabaseProvider 使用同一解析逻辑。</summary>
    public SqliteConfigurationSource(string? databasePath = null)
    {
        var root = Environment.GetEnvironmentVariable("FortOS_DATA_ROOT");
        var dataRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(root) ? "/srv/nas" : root);
        _databasePath = databasePath ?? Path.GetFullPath(Path.Combine(dataRoot, "database", "nas.db"));
    }

    /// <summary>无参构造（供 <see cref="ConfigurationExtensions.Add{TSource}"/> 使用）。</summary>
    public SqliteConfigurationSource()
        : this(databasePath: null)
    {
    }

    /// <inheritdoc />
    public IConfigurationProvider Build(IConfigurationBuilder builder) => new SqliteConfigurationProvider(_databasePath);
}

/// <summary>从 api_config 表读取覆盖值的配置提供者。</summary>
public sealed class SqliteConfigurationProvider : ConfigurationProvider
{
    private readonly string _databasePath;

    /// <summary>初始化提供者。</summary>
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
            // 数据库未初始化或不可读（如损坏）时不阻塞配置系统启动，保持空覆盖集；
            // 后续 Put 写入会重建表并触发 Reload。
            data.Clear();
        }

        Data = data;
    }
}
