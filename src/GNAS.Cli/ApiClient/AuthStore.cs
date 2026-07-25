using System.Text.Json;

namespace GNAS.Cli.ApiClient;

/// <summary>管理本机 GNAS CLI 认证资料。</summary>
public sealed class AuthStore
{
    /// <summary>已保存的服务器 URL。</summary>
    public string? Server { get; init; }

    /// <summary>已保存的访问令牌或刷新令牌。</summary>
    public string? Token { get; init; }

    /// <summary>返回默认配置文件路径。</summary>
    public static string ConfigPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gnas", "config.json");

    /// <summary>读取本机认证资料，无法读取时返回空资料。</summary>
    public static AuthStore Load()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return new AuthStore();
            using var stream = File.OpenRead(ConfigPath);
            return JsonSerializer.Deserialize<AuthStore>(stream, ApiJson.Options) ?? new AuthStore();
        }
        catch
        {
            return new AuthStore();
        }
    }

    /// <summary>保存服务器 URL 与令牌。</summary>
    public static void Save(string? server, string? token)
    {
        var path = ConfigPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(new AuthStore { Server = server, Token = token }, ApiJson.Options);
        File.WriteAllText(path, json + Environment.NewLine);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    /// <summary>清除已保存的令牌但保留服务器 URL。</summary>
    public static void SaveToken(string? server, string? token) => Save(server, token);

}
