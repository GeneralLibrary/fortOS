using YamlDotNet.RepresentationModel;

namespace GNAS.Core;

/// <summary>
/// 基于 YAML 文件的 GNAS 配置读取器。
/// </summary>
public sealed class GnasConfiguration : IGnasConfiguration
{
    private const string DefaultConfigPath = "/srv/nas/config/nas.yaml";
    private readonly string _configPath;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string[]> _arrays = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 初始化配置读取器。
    /// </summary>
    /// <param name="configPath">配置文件路径，为空时读取 GNAS_CONFIG_PATH 或使用默认路径。</param>
    public GnasConfiguration(string? configPath = null)
    {
        var path = string.IsNullOrWhiteSpace(configPath)
            ? Environment.GetEnvironmentVariable("GNAS_CONFIG_PATH")
            : configPath;
        _configPath = string.IsNullOrWhiteSpace(path) ? DefaultConfigPath : path;
        LoadConfiguration();
    }

    /// <inheritdoc />
    public string? GetValue(string key)
        => _values.TryGetValue(key, out var value) ? value : null;

    /// <inheritdoc />
    public string[] GetArray(string key)
        => _arrays.TryGetValue(key, out var values) ? values : [];

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> GetSection(string key)
    {
        var prefix = key.EndsWith(':') ? key : key + ":";
        return _values
            .Where(pair => pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(pair => pair.Key[prefix.Length..], pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task ReloadAsync(CancellationToken ct)
    {
        await _reloadLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await Task.Run(LoadConfiguration, ct).ConfigureAwait(false);
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    private void LoadConfiguration()
    {
        if (!File.Exists(_configPath))
        {
            _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _arrays = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        using var stream = File.OpenRead(_configPath);
        using var reader = new StreamReader(stream);
        var yaml = new YamlStream();
        yaml.Load(reader);

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var arrays = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (yaml.Documents.Count > 0 && yaml.Documents[0].RootNode is not null)
        {
            FlattenNode(yaml.Documents[0].RootNode, string.Empty, values, arrays);
        }

        _values = values;
        _arrays = arrays;
    }

    private static void FlattenNode(YamlNode node, string prefix, IDictionary<string, string> values, IDictionary<string, string[]> arrays)
    {
        switch (node)
        {
            case YamlMappingNode mapping:
                foreach (var child in mapping.Children)
                {
                    var key = ((YamlScalarNode)child.Key).Value ?? string.Empty;
                    var next = string.IsNullOrEmpty(prefix) ? key : $"{prefix}:{key}";
                    FlattenNode(child.Value, next, values, arrays);
                }
                break;
            case YamlSequenceNode sequence:
                var scalars = sequence.Children.OfType<YamlScalarNode>().Select(s => s.Value ?? string.Empty).ToArray();
                arrays[prefix] = scalars;
                values[prefix] = string.Join(',', scalars);
                break;
            case YamlScalarNode scalar:
                values[prefix] = scalar.Value ?? string.Empty;
                break;
        }
    }
}
