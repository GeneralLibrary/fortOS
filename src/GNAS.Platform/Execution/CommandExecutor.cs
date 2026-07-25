using System.Diagnostics;
using GNAS.Core;
using Microsoft.Extensions.Logging;

namespace GNAS.Platform.Execution;

/// <summary>
/// 执行平台命令并收集输出的内部辅助器。
/// </summary>
internal sealed class CommandExecutor
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private readonly ILogger _logger;

    /// <summary>初始化命令执行器。</summary>
    /// <param name="logger">日志记录器。</param>
    public CommandExecutor(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>执行命令并返回结果。</summary>
    /// <param name="fileName">可执行文件。</param>
    /// <param name="arguments">命令参数。</param>
    /// <param name="ct">取消令牌。</param>
    /// <param name="timeout">执行超时。</param>
    /// <param name="throwOnNonZeroExit">是否在非零退出码时抛出异常。</param>
    /// <param name="workingDirectory">工作目录。</param>
    /// <param name="environment">环境变量。</param>
    /// <param name="standardInput">标准输入内容。</param>
    /// <returns>命令执行结果。</returns>
    public async Task<CommandResult> ExecuteAsync(
        string fileName,
        string? arguments,
        CancellationToken ct,
        TimeSpan? timeout = null,
        bool throwOnNonZeroExit = true,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null,
        string? standardInput = null)
    {
        using var timeoutCts = new CancellationTokenSource(timeout ?? DefaultTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        // 日志安全：去除换行防止日志伪造；携带标准输入（通常为凭据）的命令不记录参数原文。
        var safeArguments = standardInput is null ? SanitizeForLog(arguments) : "<redacted>";

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments ?? string.Empty,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        if (environment is not null)
        {
            foreach (var pair in environment)
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        try
        {
            if (!process.Start())
            {
                throw new PlatformException($"无法启动命令: {fileName}");
            }
        }
        catch (Exception ex) when (ex is not PlatformException)
        {
            _logger.LogError(ex, "启动命令失败: {FileName} {Arguments}", fileName, safeArguments);
            throw new PlatformException($"启动命令失败: {fileName}", innerException: ex);
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
            var result = new CommandResult { ExitCode = process.ExitCode, Stdout = stdout, Stderr = stderr };

            if (process.ExitCode != 0)
            {
                _logger.LogError("命令失败: {FileName} {Arguments}, ExitCode={ExitCode}, Stderr={Stderr}", fileName, safeArguments, process.ExitCode, SanitizeForLog(stderr));
                if (throwOnNonZeroExit)
                {
                    throw new CommandExecutionException($"命令执行失败: {fileName}", process.ExitCode, stdout, stderr);
                }
            }
            else
            {
                _logger.LogInformation("命令成功: {FileName} {Arguments}", fileName, safeArguments);
            }

            return result;
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            TryKill(process);
            var stdout = await SafeReadAsync(stdoutTask).ConfigureAwait(false);
            var stderr = await SafeReadAsync(stderrTask).ConfigureAwait(false);
            _logger.LogError(ex, "命令超时: {FileName} {Arguments}", fileName, safeArguments);
            throw new CommandExecutionException($"命令执行超时: {fileName}", -1, stdout, stderr, innerException: ex);
        }
    }

    /// <summary>去除换行符，防止外部输入进入日志时伪造多行记录。</summary>
    private static string SanitizeForLog(string? value)
        => value?.ReplaceLineEndings(" ") ?? string.Empty;

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
        }
    }
}
