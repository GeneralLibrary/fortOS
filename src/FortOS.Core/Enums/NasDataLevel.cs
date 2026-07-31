namespace FortOS.Core;

/// <summary>
/// NAS data classification level.
/// </summary>
public enum NasDataLevel
{
    /// <summary>
    /// Public data.
    /// </summary>
    Public = 0,
    /// <summary>
    /// Internal data.
    /// </summary>
    Internal = 1,
    /// <summary>
    /// Personal data.
    /// </summary>
    Personal = 2,
    /// <summary>
    /// Sensitive data.
    /// </summary>
    Sensitive = 3,
    /// <summary>
    /// System data.
    /// </summary>
    System = 4,
}
