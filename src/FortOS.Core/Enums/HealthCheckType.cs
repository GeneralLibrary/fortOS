namespace FortOS.Core;

/// <summary>
/// Health check type.
/// </summary>
public enum HealthCheckType
{
    /// <summary>
    /// HTTP GET probe.
    /// </summary>
    HttpGet,
    /// <summary>
    /// TCP connection probe.
    /// </summary>
    TcpConnect,
    /// <summary>
    /// Command execution probe.
    /// </summary>
    ExecCommand,
    /// <summary>
    /// gRPC probe.
    /// </summary>
    Grpc,
}
