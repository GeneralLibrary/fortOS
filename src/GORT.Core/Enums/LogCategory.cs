namespace GORT.Core;

/// <summary>
/// Log category.
/// </summary>
public enum LogCategory
{
    /// <summary>
    /// System runtime logs.
    /// </summary>
    System,
    /// <summary>
    /// Security audit logs.
    /// </summary>
    Audit,
    /// <summary>
    /// Access logs.
    /// </summary>
    Access,
    /// <summary>
    /// Agent logs.
    /// </summary>
    Agent,
    /// <summary>
    /// Trace logs.
    /// </summary>
    Trace,
    /// <summary>
    /// Metric logs.
    /// </summary>
    Metric,
}
