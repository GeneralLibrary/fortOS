namespace GNAS.Core;

/// <summary>
/// 平台调用异常。
/// </summary>
public class PlatformException : GnasException
{
    /// <summary>初始化 平台调用异常。</summary>
    /// <param name="message">异常消息。</param>
    /// <param name="errorCode">错误码。</param>
    /// <param name="traceId">链路追踪标识。</param>
    /// <param name="innerException">内部异常。</param>
    public PlatformException(string message, string errorCode = "PLATFORM_ERROR", string? traceId = null, Exception? innerException = null)
        : base(message, errorCode, traceId, innerException)
    {
    }
}
