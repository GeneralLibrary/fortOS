namespace FortOS.Installer.Core.Exceptions;

/// <summary>安装器基类异常。携带失败发生的阶段,便于 UI/CLI 展示与重试定位。</summary>
public class InstallerException : Exception
{
    public InstallerException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>系统工具调用失败(非零退出码、超时或输出无法解析)。</summary>
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

/// <summary>安装步骤执行失败。</summary>
public class StepException : InstallerException
{
    public StepException(string step, string message, Exception? innerException = null)
        : base($"[{step}] {message}", innerException)
    {
        Step = step;
    }

    public string Step { get; }
}

/// <summary>配置校验失败(install.yaml 非法或缺字段)。</summary>
public class ConfigException : InstallerException
{
    public ConfigException(string message)
        : base(message)
    {
    }
}
