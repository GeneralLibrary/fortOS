namespace FortOS.Core;

/// <summary>NAS module base interface.</summary>
public interface INasModule
{
    /// <summary>Unique module ID.</summary>
    string ModuleId { get; }
    /// <summary>Module display name.</summary>
    string DisplayName { get; }
    /// <summary>Module version.</summary>
    Version Version { get; }
    /// <summary>Capabilities required by the module.</summary>
    IReadOnlyList<string> RequiredCapabilities { get; }
    /// <summary>Dependent module IDs.</summary>
    IReadOnlyList<string> Dependencies { get; }
    /// <summary>Initialize the module.</summary>
    Task InitializeAsync(ModuleContext context, CancellationToken ct);
    /// <summary>Gracefully shut down the module.</summary>
    Task ShutdownAsync(CancellationToken ct);
    /// <summary>Check module health status.</summary>
    Task<HealthStatus> CheckHealthAsync(CancellationToken ct);
}
