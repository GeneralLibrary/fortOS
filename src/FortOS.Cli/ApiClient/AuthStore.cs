using System.Text.Json;

namespace FortOS.Cli.ApiClient;

/// <summary>Manages local FortOS CLI authentication data.</summary>
public sealed class AuthStore
{
    /// <summary>Saved server URL.</summary>
    public string? Server { get; init; }

    /// <summary>Saved access token or refresh token.</summary>
    public string? Token { get; init; }

    /// <summary>Returns the default configuration file path.</summary>
    public static string ConfigPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fortos", "config.json");

    /// <summary>Reads local authentication data, returns empty data on failure.</summary>
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

    /// <summary>Saves server URL and token.</summary>
    public static void Save(string? server, string? token)
    {
        var path = ConfigPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(new AuthStore { Server = server, Token = token }, ApiJson.Options);
        File.WriteAllText(path, json + Environment.NewLine);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    /// <summary>Clears saved token but preserves server URL.</summary>
    public static void SaveToken(string? server, string? token) => Save(server, token);

}
