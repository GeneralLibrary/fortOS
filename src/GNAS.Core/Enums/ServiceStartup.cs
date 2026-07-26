namespace GNAS.Core;

/// <summary>
/// Service startup policy.
/// </summary>
public enum ServiceStartup
{
    /// <summary>
    /// Start automatically with the system.
    /// </summary>
    Automatic,
    /// <summary>
    /// Manual start.
    /// </summary>
    Manual,
    /// <summary>
    /// Disabled.
    /// </summary>
    Disabled,
}
