using GNAS.Core;
using GNAS.Modules.Host;
using GNAS.Modules.Update.Services;

namespace GNAS.Modules.Update;

/// <summary>Update module, providing version checking, OTA, and module hot-replacement.</summary>
public sealed class UpdateModule : NasModuleBase
{
    /// <inheritdoc />
    public override string ModuleId => "update";

    /// <inheritdoc />
    public override string DisplayName => "System Update";

    /// <inheritdoc />
    public override IReadOnlyList<string> RequiredCapabilities => ["system:update:read", "system:update:write", "module:reload"];

    /// <summary>Hot-replace a module.</summary>
    public async Task<INasModule> ReplaceModuleAsync(string moduleId, string modulePath, CancellationToken ct)
    {
        var host = RequiredService<IModuleHost>();
        await host.UnloadModuleAsync(moduleId, ct).ConfigureAwait(false);
        var module = await host.LoadModuleAsync(modulePath, ct).ConfigureAwait(false);
        await PublishAsync("system.module.replaced", "system.module.replaced", new { moduleId, modulePath }, ct).ConfigureAwait(false);
        return module;
    }
}
