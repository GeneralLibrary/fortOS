namespace GNAS.Core;

/// <summary>
/// 令牌验证异常。
/// </summary>
public class TokenValidationException : GnasException
{
    /// <summary>初始化 令牌验证异常。</summary>
    /// <param name="message">异常消息。</param>
    /// <param name="errorCode">错误码。</param>
    /// <param name="traceId">链路追踪标识。</param>
    /// <param name="innerException">内部异常。</param>
    public TokenValidationException(string message, string errorCode = "TOKEN_INVALID", string? traceId = null, Exception? innerException = null)
        : base(message, errorCode, traceId, innerException)
    {
    }
}
