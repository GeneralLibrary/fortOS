using System.Reflection;
using System.Runtime.Loader;
using GORT.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GORT.Modules.Host;

/// <summary>NAS module host, responsible for the lifecycle of built-in modules and DLL modules.</summary>
public sealed class ModuleHost : IModuleHost, IDisposable
{
    private readonly IServiceProvider services;
    private readonly IEventBus eventBus;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<ModuleHost> logger;
    private readonly List<INasModule> builtIns;
    private readonly Dictionary<string, LoadedModule> loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly object syncRoot = new();
    private bool disposed;

    /// <summary>Initialize the module host.</summary>
    public ModuleHost(IServiceProvider services, IEventBus eventBus, ILoggerFactory loggerFactory, IEnumerable<INasModule>? builtInModules = null)
    {
        this.services = services;
        this.eventBus = eventBus;
        this.loggerFactory = loggerFactory;
        logger = loggerFactory.CreateLogger<ModuleHost>();
        builtIns = builtInModules?.ToList() ?? [];
    }

    /// <summary>Register in-process modules.</summary>
    public void RegisterBuiltInModule(INasModule module)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(module);
        lock (syncRoot)
        {
            if (builtIns.Any(m => string.Equals(m.ModuleId, module.ModuleId, StringComparison.OrdinalIgnoreCase)) || loaded.ContainsKey(module.ModuleId))
            {
                throw new InvalidOperationException($"Module {module.ModuleId} is already registered or loaded.");
            }

            builtIns.Add(module);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<INasModule>> DiscoverAndLoadAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        foreach (var module in TopologicalOrder(builtIns))
        {
            await InitializeBuiltInAsync(module, ct).ConfigureAwait(false);
        }

        var available = Path.Combine(GetRootDirectory(), "modules", "available");
        var disabled = Path.Combine(GetRootDirectory(), "modules", "disabled");
        var loadedDir = Path.Combine(GetRootDirectory(), "modules", "loaded");
        Directory.CreateDirectory(available);
        Directory.CreateDirectory(disabled);
        Directory.CreateDirectory(loadedDir);

        var remaining = Directory.EnumerateFiles(available, "*.dll", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase).ToList();
        var progressed = true;
        while (remaining.Count > 0 && progressed)
        {
            progressed = false;
            foreach (var path in remaining.ToArray())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var candidate = CreateCandidate(path);
                    if (candidate.Module.Version is null)
                    {
                        logger.LogError("Skipping module {Path}: Version is null.", path);
                        candidate.Dispose();
                        remaining.Remove(path);
                        progressed = true;
                        continue;
                    }

                    if (candidate.Module.Dependencies.Any(d => !loaded.ContainsKey(d)))
                    {
                        candidate.Dispose();
                        continue;
                    }

                    await InitializeCandidateAsync(candidate, ct).ConfigureAwait(false);
                    remaining.Remove(path);
                    progressed = true;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to load module {Path}.", path);
                    remaining.Remove(path);
                    progressed = true;
                }
            }
        }

        foreach (var path in remaining)
        {
            logger.LogError("Skipping module {Path}: missing dependencies or circular dependency detected.", path);
        }

        return await GetLoadedModulesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<INasModule> LoadModuleAsync(string path, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var candidate = CreateCandidate(path);
        try
        {
            if (candidate.Module.Version is null)
            {
                throw new InvalidOperationException($"Module {path} has a null Version.");
            }

            var missing = candidate.Module.Dependencies.Where(d => !loaded.ContainsKey(d)).ToArray();
            if (missing.Length > 0)
            {
                throw new InvalidOperationException($"Module {candidate.Module.ModuleId} has missing dependencies: {string.Join(", ", missing)}.");
            }

            await InitializeCandidateAsync(candidate, ct).ConfigureAwait(false);
            return candidate.Module;
        }
        catch
        {
            candidate.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task UnloadModuleAsync(string moduleId, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        LoadedModule? entry;
        lock (syncRoot)
        {
            if (!loaded.TryGetValue(moduleId, out entry))
            {
                return;
            }

            loaded.Remove(moduleId);
        }

        await entry.Module.ShutdownAsync(ct).ConfigureAwait(false);
        entry.Dispose();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<INasModule>> GetLoadedModulesAsync(CancellationToken ct)
    {
        lock (syncRoot)
        {
            return Task.FromResult<IReadOnlyList<INasModule>>(loaded.Values.Select(m => m.Module).ToList());
        }
    }

    /// <inheritdoc />
    public Task<INasModule?> GetModuleAsync(string moduleId, CancellationToken ct)
    {
        lock (syncRoot)
        {
            return Task.FromResult(loaded.TryGetValue(moduleId, out var entry) ? entry.Module : null);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        foreach (var entry in loaded.Values.ToArray())
        {
            try
            {
                entry.Module.ShutdownAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error shutting down module {ModuleId}.", entry.Module.ModuleId);
            }

            entry.Dispose();
        }

        loaded.Clear();
    }

    private async Task InitializeBuiltInAsync(INasModule module, CancellationToken ct)
    {
        if (loaded.ContainsKey(module.ModuleId))
        {
            return;
        }

        var missing = module.Dependencies.Where(d => !loaded.ContainsKey(d)).ToArray();
        if (missing.Length > 0)
        {
            logger.LogError("Skipping built-in module {ModuleId}: missing dependencies {Dependencies}.", module.ModuleId, string.Join(", ", missing));
            return;
        }

        await module.InitializeAsync(CreateContext(module.ModuleId), ct).ConfigureAwait(false);
        lock (syncRoot)
        {
            loaded[module.ModuleId] = new LoadedModule(module, null);
        }
    }

    private async Task InitializeCandidateAsync(LoadedModule candidate, CancellationToken ct)
    {
        var module = candidate.Module;
        if (loaded.ContainsKey(module.ModuleId))
        {
            throw new InvalidOperationException($"Module {module.ModuleId} is already loaded.");
        }

        await module.InitializeAsync(CreateContext(module.ModuleId), ct).ConfigureAwait(false);
        lock (syncRoot)
        {
            loaded[module.ModuleId] = candidate;
        }
    }

    private ModuleContext CreateContext(string moduleId) => new()
    {
        Services = services,
        EventBus = eventBus,
        LoggerFactory = loggerFactory,
        DataDirectory = Path.Combine(GetRootDirectory(), "modules", "loaded", moduleId)
    };

    private static string GetRootDirectory() => Environment.GetEnvironmentVariable("GORT_DATA_ROOT") is { Length: > 0 } root ? root : "/srv/nas";

    private LoadedModule CreateCandidate(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var alc = new ModuleAssemblyLoadContext(fullPath);
        var assembly = alc.LoadFromAssemblyPath(fullPath);
        var moduleType = assembly.GetTypes().FirstOrDefault(t => !t.IsAbstract && typeof(INasModule).IsAssignableFrom(t))
            ?? throw new InvalidOperationException($"No INasModule implementation found in {path}.");
        var module = Activator.CreateInstance(moduleType) as INasModule
            ?? throw new InvalidOperationException($"Type {moduleType.FullName} could not be created as INasModule.");
        return new LoadedModule(module, alc);
    }

    private static IReadOnlyList<INasModule> TopologicalOrder(IEnumerable<INasModule> modules)
    {
        var moduleList = modules.GroupBy(m => m.ModuleId, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        var resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<INasModule>();
        var pending = moduleList.ToList();
        var progressed = true;
        while (pending.Count > 0 && progressed)
        {
            progressed = false;
            foreach (var module in pending.ToArray())
            {
                if (module.Dependencies.All(d => resolved.Contains(d) || moduleList.All(m => !string.Equals(m.ModuleId, d, StringComparison.OrdinalIgnoreCase))))
                {
                    ordered.Add(module);
                    resolved.Add(module.ModuleId);
                    pending.Remove(module);
                    progressed = true;
                }
            }
        }

        ordered.AddRange(pending);
        return ordered;
    }

    private sealed class ModuleAssemblyLoadContext(string modulePath) : AssemblyLoadContext(isCollectible: true)
    {
        private readonly AssemblyDependencyResolver resolver = new(modulePath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var assemblyPath = resolver.ResolveAssemblyToPath(assemblyName);
            return assemblyPath is null ? null : LoadFromAssemblyPath(assemblyPath);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var libraryPath = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return libraryPath is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(libraryPath);
        }
    }

    private sealed class LoadedModule(INasModule module, AssemblyLoadContext? loadContext) : IDisposable
    {
        public INasModule Module { get; } = module;

        public void Dispose()
        {
            loadContext?.Unload();
        }
    }
}
