namespace GNAS.Core;

/// <summary>
/// GNAS 基础异常，携带统一错误码和链路追踪标识。
/// </summary>
public class GnasException : Exception
{
    /// <summary>错误码。</summary>
    public string ErrorCode { get; }

    /// <summary>链路追踪标识。</summary>
    public string? TraceId { get; }

    /// <summary>初始化 GNAS 异常。</summary>
    /// <param name="message">异常消息。</param>
    /// <param name="errorCode">错误码。</param>
    /// <param name="traceId">链路追踪标识。</param>
    /// <param name="innerException">内部异常。</param>
    public GnasException(string message, string errorCode, string? traceId = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        TraceId = traceId;
    }
}
