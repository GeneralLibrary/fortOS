namespace GORT.Core;

/// <summary>
/// Service running status.
/// </summary>
public enum ServiceStatus
{
    /// <summary>
    /// Stopped.
    /// </summary>
    Stopped,
    /// <summary>
    /// Starting.
    /// </summary>
    Starting,
    /// <summary>
    /// Running.
    /// </summary>
    Running,
    /// <summary>
    /// Stopping.
    /// </summary>
    Stopping,
    /// <summary>
    /// Failed.
    /// </summary>
    Failed,
    /// <summary>
    /// Unknown.
    /// </summary>
    Unknown,
}
