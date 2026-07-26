namespace GNAS.Core;

/// <summary>
/// Exception thrown when token validation fails.
/// </summary>
public class TokenValidationException : GnasException
{
    /// <summary>Initialize a token validation exception.</summary>
    /// <param name="message">Exception message.</param>
    /// <param name="errorCode">Error code.</param>
    /// <param name="traceId">Trace ID.</param>
    /// <param name="innerException">Inner exception.</param>
    public TokenValidationException(string message, string errorCode = "TOKEN_INVALID", string? traceId = null, Exception? innerException = null)
        : base(message, errorCode, traceId, innerException)
    {
    }
}
