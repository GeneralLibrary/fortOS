namespace GNAS.Core;

/// <summary>
/// 健康检查类型。
/// </summary>
public enum HealthCheckType
{
    /// <summary>
    /// HTTP GET 探针。
    /// </summary>
    HttpGet,
    /// <summary>
    /// TCP 连接探针。
    /// </summary>
    TcpConnect,
    /// <summary>
    /// 命令执行探针。
    /// </summary>
    ExecCommand,
    /// <summary>
    /// gRPC 探针。
    /// </summary>
    Grpc,
}
