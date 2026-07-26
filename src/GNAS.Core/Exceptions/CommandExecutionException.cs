namespace GNAS.Core;

/// <summary>
/// Exception thrown when a command execution fails.
/// </summary>
public class CommandExecutionException : GnasException
{
    /// <summary>Process exit code.</summary>
    public int ExitCode { get; }

    /// <summary>Standard output.</summary>
    public string Stdout { get; }

    /// <summary>Standard error.</summary>
    public string Stderr { get; }

    /// <summary>Initialize a command execution exception.</summary>
    /// <param name="message">Exception message.</param>
    /// <param name="exitCode">Exit code.</param>
    /// <param name="stdout">Standard output.</param>
    /// <param name="stderr">Standard error.</param>
    /// <param name="errorCode">Error code.</param>
    /// <param name="traceId">Trace ID.</param>
    /// <param name="innerException">Inner exception.</param>
    public CommandExecutionException(string message, int exitCode, string stdout, string stderr, string errorCode = "COMMAND_EXECUTION_FAILED", string? traceId = null, Exception? innerException = null)
        : base(message, errorCode, traceId, innerException)
    {
        ExitCode = exitCode;
        Stdout = stdout;
        Stderr = stderr;
    }
}
