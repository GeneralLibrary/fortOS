namespace GNAS.Core;

/// <summary>
/// 服务不存在异常。
/// </summary>
public class ServiceNotFoundException : GnasException
{
    /// <summary>初始化 服务不存在异常。</summary>
    /// <param name="message">异常消息。</param>
    /// <param name="errorCode">错误码。</param>
    /// <param name="traceId">链路追踪标识。</param>
    /// <param name="innerException">内部异常。</param>
    public ServiceNotFoundException(string message, string errorCode = "SERVICE_NOT_FOUND", string? traceId = null, Exception? innerException = null)
        : base(message, errorCode, traceId, innerException)
    {
    }
}
