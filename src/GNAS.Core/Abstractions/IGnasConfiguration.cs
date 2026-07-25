namespace GNAS.Core;

/// <summary>GNAS 配置读取接口。</summary>
public interface IGnasConfiguration
{
    /// <summary>获取字符串配置值。</summary>
    string? GetValue(string key);
    /// <summary>获取数组配置值。</summary>
    string[] GetArray(string key);
    /// <summary>获取配置段。</summary>
    IReadOnlyDictionary<string, string> GetSection(string key);
    /// <summary>重新加载配置。</summary>
    Task ReloadAsync(CancellationToken ct);
}
