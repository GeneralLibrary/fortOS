namespace FortOS.Core;

/// <summary>
/// FortOS base exception carrying a unified error code and trace ID.
/// </summary>
public class FortOSException : Exception
{
    /// <summary>Error code.</summary>
    public string ErrorCode { get; }

    /// <summary>Trace ID.</summary>
    public string? TraceId { get; }

    /// <summary>Initialize a FortOS exception.</summary>
    /// <param name="message">Exception message.</param>
    /// <param name="errorCode">Error code.</param>
    /// <param name="traceId">Trace ID.</param>
    /// <param name="innerException">Inner exception.</param>
    public FortOSException(string message, string errorCode, string? traceId = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        TraceId = traceId;
    }
}
