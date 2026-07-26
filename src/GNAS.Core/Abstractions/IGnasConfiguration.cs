namespace GNAS.Core;

/// <summary>GNAS configuration read interface.</summary>
public interface IGnasConfiguration
{
    /// <summary>Get a string configuration value.</summary>
    string? GetValue(string key);
    /// <summary>Get an array configuration value.</summary>
    string[] GetArray(string key);
    /// <summary>Get a configuration section.</summary>
    IReadOnlyDictionary<string, string> GetSection(string key);
    /// <summary>Reload configuration.</summary>
    Task ReloadAsync(CancellationToken ct);
}
