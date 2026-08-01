namespace FortOS.Agent.Infrastructure;

/// <summary>
/// Provides data directory paths used by the Agent integration layer.
/// </summary>
public static class AgentPaths
{
    private const string DefaultDataRoot = "/srv/nas";

    /// <summary>
    /// Gets the FortOS data root directory.
    /// </summary>
    public static string DataRoot => Path.GetFullPath(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FortOS_DATA_ROOT"))
        ? DefaultDataRoot
        : Environment.GetEnvironmentVariable("FortOS_DATA_ROOT")!);

    /// <summary>
    /// Gets the Agent root directory.
    /// </summary>
    public static string AgentsRoot => Path.Combine(DataRoot, "agents");

    /// <summary>
    /// Gets the template catalog directory.
    /// </summary>
    public static string CatalogRoot => Path.Combine(AgentsRoot, "catalog");
}
