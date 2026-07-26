namespace GNAS.Core;

/// <summary>
/// Service restart policy.
/// </summary>
public enum RestartPolicy
{
    /// <summary>
    /// Always restart.
    /// </summary>
    Always,
    /// <summary>
    /// Restart on failure.
    /// </summary>
    OnFailure,
    /// <summary>
    /// Never restart automatically.
    /// </summary>
    Never,
    /// <summary>
    /// Restart with exponential backoff.
    /// </summary>
    ExponentialBackoff,
}
