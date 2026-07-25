namespace GNAS.Core;

/// <summary>
/// 命令执行失败异常。
/// </summary>
public class CommandExecutionException : GnasException
{
    /// <summary>进程退出码。</summary>
    public int ExitCode { get; }

    /// <summary>标准输出。</summary>
    public string Stdout { get; }

    /// <summary>标准错误。</summary>
    public string Stderr { get; }

    /// <summary>初始化命令执行异常。</summary>
    /// <param name="message">异常消息。</param>
    /// <param name="exitCode">退出码。</param>
    /// <param name="stdout">标准输出。</param>
    /// <param name="stderr">标准错误。</param>
    /// <param name="errorCode">错误码。</param>
    /// <param name="traceId">链路追踪标识。</param>
    /// <param name="innerException">内部异常。</param>
    public CommandExecutionException(string message, int exitCode, string stdout, string stderr, string errorCode = "COMMAND_EXECUTION_FAILED", string? traceId = null, Exception? innerException = null)
        : base(message, errorCode, traceId, innerException)
    {
        ExitCode = exitCode;
        Stdout = stdout;
        Stderr = stderr;
    }
}
