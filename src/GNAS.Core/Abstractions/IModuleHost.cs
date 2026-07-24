namespace GNAS.Core;

/// <summary>模块宿主接口。</summary>
public interface IModuleHost
{
    /// <summary>发现并加载模块。</summary>
    Task<IReadOnlyList<INasModule>> DiscoverAndLoadAsync(CancellationToken ct);
    /// <summary>按路径加载模块。</summary>
    Task<INasModule> LoadModuleAsync(string path, CancellationToken ct);
    /// <summary>卸载模块。</summary>
    Task UnloadModuleAsync(string moduleId, CancellationToken ct);
    /// <summary>获取已加载模块。</summary>
    Task<IReadOnlyList<INasModule>> GetLoadedModulesAsync(CancellationToken ct);
    /// <summary>获取指定模块。</summary>
    Task<INasModule?> GetModuleAsync(string moduleId, CancellationToken ct);
}
