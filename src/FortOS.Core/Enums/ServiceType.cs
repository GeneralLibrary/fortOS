namespace FortOS.Core;

/// <summary>
/// Service host type.
/// </summary>
public enum ServiceType
{
    /// <summary>
    /// Native OS process.
    /// </summary>
    Native,
    /// <summary>
    /// Containerized service.
    /// </summary>
    Container,
    /// <summary>
    /// .NET in-process module.
    /// </summary>
    Module,
    /// <summary>
    /// Linux system service managed by systemd.
    /// </summary>
    Systemd,
}
