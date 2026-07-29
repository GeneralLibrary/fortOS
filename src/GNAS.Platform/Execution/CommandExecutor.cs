using System.Diagnostics;
using GNAS.Core;
using Microsoft.Extensions.Logging;

namespace GNAS.Platform.Execution;

/// <summary>
/// Internal helper for executing platform commands and collecting output.
/// </summary>
internal sealed class CommandExecutor
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private readonly ILogger _logger;

    /// <summary>Initializes the command executor.</summary>
    /// <param name="logger">Logger.</param>
    public CommandExecutor(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>Executes a command and returns the result.</summary>
    /// <param name="fileName">Executable file.</param>
    /// <param name="arguments">Command arguments.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="timeout">Execution timeout.</param>
    /// <param name="throwOnNonZeroExit">Whether to throw an exception on non-zero exit codes.</param>
    /// <param name="workingDirectory">Working directory.</param>
    /// <param name="environment">Environment variables.</param>
    /// <param name="standardInput">Standard input content.</param>
    /// <returns>Command execution result.</returns>
    public async Task<CommandResult> ExecuteAsync(
        string fileName,
        string? arguments,
        CancellationToken ct,
        TimeSpan? timeout = null,
        bool throwOnNonZeroExit = true,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null,
        string? standardInput = null,
        bool logResult = true)
    {
        using var timeoutCts = new CancellationTokenSource(timeout ?? DefaultTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        // Log safety: strip newlines to prevent log forgery; commands with standard input (usually credentials) do not log the original arguments.
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
                throw new PlatformException($"Failed to start command: {fileName}");
            }
        }
        catch (Exception ex) when (ex is not PlatformException)
        {
            _logger.LogError(ex, "Failed to start command: {FileName} {Arguments}", fileName, safeArguments);
            throw new PlatformException($"Failed to start command: {fileName}", innerException: ex);
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
                if (logResult)
                {
                    _logger.LogError("Command failed: {FileName} {Arguments}, ExitCode={ExitCode}, Stderr={Stderr}", fileName, safeArguments, process.ExitCode, SanitizeForLog(stderr));
                }
                if (throwOnNonZeroExit)
                {
                    throw new CommandExecutionException($"Command execution failed: {fileName}", process.ExitCode, stdout, stderr);
                }
            }
            else if (logResult)
            {
                _logger.LogInformation("Command succeeded: {FileName} {Arguments}", fileName, safeArguments);
            }

            return result;
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            TryKill(process);
            var stdout = await SafeReadAsync(stdoutTask).ConfigureAwait(false);
            var stderr = await SafeReadAsync(stderrTask).ConfigureAwait(false);
            _logger.LogError(ex, "Command timed out: {FileName} {Arguments}", fileName, safeArguments);
            throw new CommandExecutionException($"Command execution timed out: {fileName}", -1, stdout, stderr, innerException: ex);
        }
    }

    /// <summary>Strips newlines to prevent external input from forging multi-line log entries.</summary>
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
