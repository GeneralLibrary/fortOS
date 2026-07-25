using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using GNAS.Core;
using GNAS.Platform.Execution;
using Microsoft.Extensions.Logging;

namespace GNAS.Platform.Windows;

/// <summary>
/// Windows 进程管理器。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsProcessManager : IProcessManager
{
    private readonly CommandExecutor _executor;

    /// <summary>初始化 Windows 进程管理器。</summary>
    /// <param name="logger">日志记录器。</param>
    public WindowsProcessManager(ILogger<WindowsProcessManager> logger) => _executor = new CommandExecutor(logger);

    /// <inheritdoc />
    public Task<ProcessInfo> StartProcessAsync(ProcessStartConfig config, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo { FileName = config.ExecutablePath, Arguments = config.Arguments ?? string.Empty, UseShellExecute = false };
        var process = Process.Start(startInfo) ?? throw new PlatformException($"无法启动进程: {config.ExecutablePath}");
        return Task.FromResult(ToInfo(process));
    }

    /// <inheritdoc />
    public async Task StopProcessAsync(int pid, TimeSpan gracefulTimeout, CancellationToken ct)
    {
        var process = Process.GetProcessById(pid);
        process.CloseMainWindow();
        try { await process.WaitForExitAsync(new CancellationTokenSource(gracefulTimeout).Token).ConfigureAwait(false); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }

    /// <inheritdoc />
    public Task<ProcessInfo?> GetProcessAsync(int pid, CancellationToken ct)
    {
        try { return Task.FromResult<ProcessInfo?>(ToInfo(Process.GetProcessById(pid))); }
        catch (ArgumentException) { return Task.FromResult<ProcessInfo?>(null); }
    }

    /// <inheritdoc />
    public Task<CommandResult> ExecuteCommandAsync(ProcessStartConfig config, CancellationToken ct)
        => _executor.ExecuteAsync(config.ExecutablePath, config.Arguments, ct, TimeSpan.FromSeconds(config.TimeoutSeconds), workingDirectory: config.WorkingDirectory, environment: config.EnvironmentVariables, standardInput: config.StandardInput);

    /// <inheritdoc />
    public async Task EnableServiceAsync(string serviceName, CancellationToken ct)
    {
        ValidateServiceName(serviceName);
        await _executor.ExecuteAsync("sc.exe", $"config {Quote(serviceName)} start= auto", ct).ConfigureAwait(false);
        await _executor.ExecuteAsync("sc.exe", $"start {Quote(serviceName)}", ct, throwOnNonZeroExit: false).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DisableServiceAsync(string serviceName, CancellationToken ct)
    {
        ValidateServiceName(serviceName);
        await _executor.ExecuteAsync("sc.exe", $"stop {Quote(serviceName)}", ct, throwOnNonZeroExit: false).ConfigureAwait(false);
        await _executor.ExecuteAsync("sc.exe", $"config {Quote(serviceName)} start= disabled", ct).ConfigureAwait(false);
    }

    private static ProcessInfo ToInfo(Process p)
    {
        string? commandLine;
        try { commandLine = p.MainModule?.FileName; } catch { commandLine = null; }
        return new ProcessInfo { Pid = p.Id, ProcessName = p.ProcessName, CommandLine = commandLine, MemoryBytes = p.WorkingSet64, StartTime = p.StartTime };
    }
    private static void ValidateServiceName(string name) { if (!ServiceNameRegex().IsMatch(name)) throw new ArgumentException("服务名称不安全。", nameof(name)); }
    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    [GeneratedRegex("^[A-Za-z0-9_.@-]+$")]
    private static partial Regex ServiceNameRegex();
}
