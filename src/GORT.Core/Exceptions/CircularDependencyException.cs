namespace GORT.Core;

/// <summary>
/// Exception thrown when a circular dependency is detected in service dependencies.
/// </summary>
public class CircularDependencyException : GortException
{
    /// <summary>Initialize a circular dependency exception.</summary>
    /// <param name="message">Exception message.</param>
    /// <param name="errorCode">Error code.</param>
    /// <param name="traceId">Trace ID.</param>
    /// <param name="innerException">Inner exception.</param>
    public CircularDependencyException(string message, string errorCode = "CIRCULAR_DEPENDENCY", string? traceId = null, Exception? innerException = null)
        : base(message, errorCode, traceId, innerException)
    {
    }
}
