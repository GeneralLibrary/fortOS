namespace GNAS.Agent.Infrastructure;

/// <summary>
/// 提供 Agent 集成层使用的数据目录路径。
/// </summary>
internal static class AgentPaths
{
    private const string DefaultDataRoot = "/srv/nas";

    /// <summary>
    /// 获取 GNAS 数据根目录。
    /// </summary>
    public static string DataRoot => Path.GetFullPath(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GNAS_DATA_ROOT"))
        ? DefaultDataRoot
        : Environment.GetEnvironmentVariable("GNAS_DATA_ROOT")!);

    /// <summary>
    /// 获取 Agent 根目录。
    /// </summary>
    public static string AgentsRoot => Path.Combine(DataRoot, "agents");

    /// <summary>
    /// 获取模板目录。
    /// </summary>
    public static string CatalogRoot => Path.Combine(AgentsRoot, "catalog");
}
