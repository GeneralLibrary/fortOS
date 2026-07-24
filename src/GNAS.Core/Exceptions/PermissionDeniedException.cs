namespace GNAS.Core;

/// <summary>
/// 权限拒绝异常。
/// </summary>
public class PermissionDeniedException : GnasException
{
    /// <summary>初始化 权限拒绝异常。</summary>
    /// <param name="message">异常消息。</param>
    /// <param name="errorCode">错误码。</param>
    /// <param name="traceId">链路追踪标识。</param>
    /// <param name="innerException">内部异常。</param>
    public PermissionDeniedException(string message, string errorCode = "PERMISSION_DENIED", string? traceId = null, Exception? innerException = null)
        : base(message, errorCode, traceId, innerException)
    {
    }
}
