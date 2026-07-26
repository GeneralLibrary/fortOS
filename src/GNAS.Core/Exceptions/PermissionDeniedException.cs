namespace GNAS.Core;

/// <summary>
/// Exception thrown when permission is denied.
/// </summary>
public class PermissionDeniedException : GnasException
{
    /// <summary>Initialize a permission denied exception.</summary>
    /// <param name="message">Exception message.</param>
    /// <param name="errorCode">Error code.</param>
    /// <param name="traceId">Trace ID.</param>
    /// <param name="innerException">Inner exception.</param>
    public PermissionDeniedException(string message, string errorCode = "PERMISSION_DENIED", string? traceId = null, Exception? innerException = null)
        : base(message, errorCode, traceId, innerException)
    {
    }
}
