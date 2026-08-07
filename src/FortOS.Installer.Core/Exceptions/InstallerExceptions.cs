namespace FortOS.Installer.Core.Exceptions;

/// <summary>Base exception for the installer. Carries the phase where the failure occurred for UI/CLI display and retry targeting.</summary>
public class InstallerException : Exception
{
    public InstallerException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>System tool invocation failed (non-zero exit code, timeout, or unparseable output).</summary>
public class ToolException : InstallerException
{
    public ToolException(string message, int exitCode, string stdout, string stderr, Exception? innerException = null)
        : base(message, innerException)
    {
        ExitCode = exitCode;
        Stdout = stdout;
        Stderr = stderr;
    }

    public int ExitCode { get; }

    public string Stdout { get; }

    public string Stderr { get; }
}

/// <summary>An installation step failed.</summary>
public class StepException : InstallerException
{
    public StepException(string step, string message, Exception? innerException = null)
        : base($"[{step}] {message}", innerException)
    {
        Step = step;
    }

    public string Step { get; }
}

/// <summary>Config validation failed (install.yaml is invalid or missing fields).</summary>
public class ConfigException : InstallerException
{
    public ConfigException(string message)
        : base(message)
    {
    }
}
