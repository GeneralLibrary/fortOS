using System.Diagnostics;
using FortOS.Installer.Core.Exceptions;

namespace FortOS.Installer.Core.Tools;

/// <summary>命令执行结果。</summary>
public sealed record CommandResult(int ExitCode, string Stdout, string Stderr);

/// <summary>进程执行抽象,便于测试注入。</summary>
public interface IProcessRunner
{
    /// <summary>
    /// 执行命令并收集输出。参数以列表传递,避免 shell 转义问题。
    /// </summary>
    /// <param name="standardInput">可选标准输入内容(密钥等敏感数据,不进命令行)。</param>
    Task<CommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken ct,
        TimeSpan? timeout = null,
        bool throwOnNonZeroExit = true,
        string? workingDirectory = null,
        string? standardInput = null);
}

/// <summary>
/// 系统工具进程执行器(设计稿 6)。行为参考 FortOS.Platform.Execution.CommandExecutor:
/// 超时、非零退出码抛 <see cref="ToolException"/>(消息附带 stderr 摘要便于诊断)。
/// 参数经 ArgumentList 传递,不使用 shell——无注入面;敏感数据(密钥)走
/// <paramref name="standardInput"/>,不进命令行。
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    /// <summary>默认命令超时(普通系统工具足够;长任务如 rsync 由调用方显式指定)。</summary>
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

    /// <summary>异常消息中 stderr 摘要的最大长度。</summary>
    private const int ErrorDetailMaxLength = 300;

    /// <summary>启动失败/超时使用的人工退出码(进程未产生真实退出码)。</summary>
    private const int SyntheticExitCode = -1;

    /// <inheritdoc />
    public async Task<CommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken ct,
        TimeSpan? timeout = null,
        bool throwOnNonZeroExit = true,
        string? workingDirectory = null,
        string? standardInput = null)
    {
        using var timeoutCts = new CancellationTokenSource(timeout ?? DefaultTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new ToolException($"Failed to start command: {fileName}", SyntheticExitCode, string.Empty, string.Empty);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(linkedCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(linkedCts.Token);
        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(standardInput.AsMemory(), linkedCts.Token).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(linkedCts.Token).ConfigureAwait(false);
            process.StandardInput.Close();
        }

        try
        {
            await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            if (process.ExitCode != 0 && throwOnNonZeroExit)
            {
                // 异常消息附带 stderr/stdout 摘要,便于 CLI/日志直接诊断失败原因。
                var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                var trimmed = detail.Length > ErrorDetailMaxLength ? detail[..ErrorDetailMaxLength] : detail;
                throw new ToolException(
                    $"Command failed: {fileName} {string.Join(' ', arguments)}\n{trimmed}",
                    process.ExitCode,
                    stdout,
                    stderr);
            }

            return new CommandResult(process.ExitCode, stdout, stderr);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // 超时:终止子进程并抛出类型化异常。
            TryKill(process);
            var stdout = await SafeReadAsync(stdoutTask).ConfigureAwait(false);
            var stderr = await SafeReadAsync(stderrTask).ConfigureAwait(false);
            throw new ToolException($"Command timed out: {fileName}", SyntheticExitCode, stdout, stderr, ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            TryKill(process);
            throw;
        }
    }

    private static async Task<string> SafeReadAsync(Task<string> task)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // 进程可能已退出。
        }
    }
}
