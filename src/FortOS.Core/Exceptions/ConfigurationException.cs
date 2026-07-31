namespace FortOS.Core;

/// <summary>
/// Exception thrown for configuration errors.
/// </summary>
public class ConfigurationException : FortOSException
{
    /// <summary>Initialize a configuration exception.</summary>
    /// <param name="message">Exception message.</param>
    /// <param name="errorCode">Error code.</param>
    /// <param name="traceId">Trace ID.</param>
    /// <param name="innerException">Inner exception.</param>
    public ConfigurationException(string message, string errorCode = "CONFIGURATION_ERROR", string? traceId = null, Exception? innerException = null)
        : base(message, errorCode, traceId, innerException)
    {
    }
}
