namespace GNAS.Core;

/// <summary>
/// GNAS base exception carrying a unified error code and trace ID.
/// </summary>
public class GnasException : Exception
{
    /// <summary>Error code.</summary>
    public string ErrorCode { get; }

    /// <summary>Trace ID.</summary>
    public string? TraceId { get; }

    /// <summary>Initialize a GNAS exception.</summary>
    /// <param name="message">Exception message.</param>
    /// <param name="errorCode">Error code.</param>
    /// <param name="traceId">Trace ID.</param>
    /// <param name="innerException">Inner exception.</param>
    public GnasException(string message, string errorCode, string? traceId = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        TraceId = traceId;
    }
}
