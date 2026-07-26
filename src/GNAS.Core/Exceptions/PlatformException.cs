namespace GNAS.Core;

/// <summary>
/// Exception thrown for platform invocation errors.
/// </summary>
public class PlatformException : GnasException
{
    /// <summary>Initialize a platform exception.</summary>
    /// <param name="message">Exception message.</param>
    /// <param name="errorCode">Error code.</param>
    /// <param name="traceId">Trace ID.</param>
    /// <param name="innerException">Inner exception.</param>
    public PlatformException(string message, string errorCode = "PLATFORM_ERROR", string? traceId = null, Exception? innerException = null)
        : base(message, errorCode, traceId, innerException)
    {
    }
}
