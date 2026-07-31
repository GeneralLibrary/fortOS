namespace FortOS.Core;

/// <summary>
/// Exception thrown when a service is not found.
/// </summary>
public class ServiceNotFoundException : FortOSException
{
    /// <summary>Initialize a service not found exception.</summary>
    /// <param name="message">Exception message.</param>
    /// <param name="errorCode">Error code.</param>
    /// <param name="traceId">Trace ID.</param>
    /// <param name="innerException">Inner exception.</param>
    public ServiceNotFoundException(string message, string errorCode = "SERVICE_NOT_FOUND", string? traceId = null, Exception? innerException = null)
        : base(message, errorCode, traceId, innerException)
    {
    }
}
