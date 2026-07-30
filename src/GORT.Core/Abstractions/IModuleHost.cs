namespace GORT.Core;

/// <summary>Module host interface.</summary>
public interface IModuleHost
{
    /// <summary>Discover and load modules.</summary>
    Task<IReadOnlyList<INasModule>> DiscoverAndLoadAsync(CancellationToken ct);
    /// <summary>Load a module by path.</summary>
    Task<INasModule> LoadModuleAsync(string path, CancellationToken ct);
    /// <summary>Unload a module.</summary>
    Task UnloadModuleAsync(string moduleId, CancellationToken ct);
    /// <summary>Get loaded modules.</summary>
    Task<IReadOnlyList<INasModule>> GetLoadedModulesAsync(CancellationToken ct);
    /// <summary>Get a specific module.</summary>
    Task<INasModule?> GetModuleAsync(string moduleId, CancellationToken ct);
}
