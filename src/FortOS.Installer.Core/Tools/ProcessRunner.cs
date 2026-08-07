using System.Diagnostics;
using FortOS.Installer.Core.Exceptions;

namespace FortOS.Installer.Core.Tools;

/// <summary>Command execution result.</summary>
public sealed record CommandResult(int ExitCode, string Stdout, string Stderr);

/// <summary>Process execution abstraction, for easy test injection.</summary>
public interface IProcessRunner
{
    /// <summary>
    /// Executes a command and collects the output. Arguments are passed as a list, avoiding shell escaping issues.
    /// </summary>
    /// <param name="standardInput">Optional standard input content (sensitive data such as secrets, not passed on the command line).</param>
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
/// Process executor for system tools (design draft 6). Behavior follows FortOS.Platform.Execution.CommandExecutor:
/// on timeout or a non-zero exit code it throws <see cref="ToolException"/> (the message includes a stderr summary for diagnosis).
/// Arguments are passed via ArgumentList, no shell is used — no injection surface; sensitive data (secrets) goes through
/// <paramref name="standardInput"/>, never on the command line.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    /// <summary>Default command timeout (sufficient for ordinary system tools; long tasks such as rsync specify their own explicitly).</summary>
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

    /// <summary>Maximum length of the stderr summary in exception messages.</summary>
    private const int ErrorDetailMaxLength = 300;

    /// <summary>Synthetic exit code used for start failures/timeouts (the process never produced a real exit code).</summary>
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
                // The exception message includes a stderr/stdout summary so the CLI/logs can diagnose the failure directly.
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
            // Timeout: kill the child process and throw a typed exception.
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
            // The process may have already exited.
        }
    }
}
