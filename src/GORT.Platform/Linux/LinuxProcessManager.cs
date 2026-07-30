using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using GORT.Core;
using GORT.Platform.Execution;
using Microsoft.Extensions.Logging;

namespace GORT.Platform.Linux;

/// <summary>
/// Linux process manager.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed partial class LinuxProcessManager : IProcessManager
{
    private readonly CommandExecutor _executor;

    /// <summary>Initializes the Linux process manager.</summary>
    /// <param name="logger">Logger.</param>
    public LinuxProcessManager(ILogger<LinuxProcessManager> logger)
    {
        _executor = new CommandExecutor(logger);
    }

    /// <inheritdoc />
    public Task<ProcessInfo> StartProcessAsync(ProcessStartConfig config, CancellationToken ct)
    {
        var info = new ProcessStartInfo
        {
            FileName = config.ExecutablePath,
            Arguments = config.Arguments ?? string.Empty,
            WorkingDirectory = config.WorkingDirectory ?? Environment.CurrentDirectory,
            UseShellExecute = false,
        };
        if (config.EnvironmentVariables is not null)
        {
            foreach (var pair in config.EnvironmentVariables)
            {
                info.Environment[pair.Key] = pair.Value;
            }
        }

        var process = Process.Start(info) ?? throw new PlatformException($"Failed to start process: {config.ExecutablePath}");
        return Task.FromResult(ToInfo(process));
    }

    /// <inheritdoc />
    public async Task StopProcessAsync(int pid, TimeSpan gracefulTimeout, CancellationToken ct)
    {
        await _executor.ExecuteAsync("kill", $"-TERM {pid}", ct, throwOnNonZeroExit: false).ConfigureAwait(false);
        var deadline = DateTimeOffset.UtcNow + gracefulTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await GetProcessAsync(pid, ct).ConfigureAwait(false) is null)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), ct).ConfigureAwait(false);
        }

        await _executor.ExecuteAsync("kill", $"-KILL {pid}", ct, throwOnNonZeroExit: false).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<ProcessInfo?> GetProcessAsync(int pid, CancellationToken ct)
    {
        try
        {
            return Task.FromResult<ProcessInfo?>(ToInfo(Process.GetProcessById(pid)));
        }
        catch (ArgumentException)
        {
            return Task.FromResult<ProcessInfo?>(null);
        }
    }

    /// <inheritdoc />
    public Task<CommandResult> ExecuteCommandAsync(ProcessStartConfig config, CancellationToken ct)
        => _executor.ExecuteAsync(config.ExecutablePath, config.Arguments, ct, TimeSpan.FromSeconds(config.TimeoutSeconds), workingDirectory: config.WorkingDirectory, environment: config.EnvironmentVariables, standardInput: config.StandardInput);

    /// <inheritdoc />
    public async Task EnableServiceAsync(string serviceName, CancellationToken ct)
    {
        ValidateServiceName(serviceName);
        var unitPath = $"/etc/systemd/system/{serviceName}.service";
        if (!File.Exists(unitPath))
        {
            await File.WriteAllTextAsync(unitPath, $"[Unit]\nDescription=GORT {serviceName}\nAfter=network.target\n\n[Service]\nType=simple\nExecStart=/usr/bin/true\nRemainAfterExit=yes\n\n[Install]\nWantedBy=multi-user.target\n", ct).ConfigureAwait(false);
        }

        await _executor.ExecuteAsync("systemctl", "daemon-reload", ct).ConfigureAwait(false);
        await _executor.ExecuteAsync("systemctl", $"enable --now {Quote(serviceName + ".service")}", ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DisableServiceAsync(string serviceName, CancellationToken ct)
    {
        ValidateServiceName(serviceName);
        await _executor.ExecuteAsync("systemctl", $"disable --now {Quote(serviceName + ".service")}", ct, throwOnNonZeroExit: false).ConfigureAwait(false);
        var unitPath = $"/etc/systemd/system/{serviceName}.service";
        if (File.Exists(unitPath))
        {
            File.Delete(unitPath);
        }

        await _executor.ExecuteAsync("systemctl", "daemon-reload", ct).ConfigureAwait(false);
    }

    private static ProcessInfo ToInfo(Process process)
    {
        DateTimeOffset startTime;
        try { startTime = process.StartTime; } catch { startTime = DateTimeOffset.MinValue; }
        long memory;
        try { memory = process.WorkingSet64; } catch { memory = 0; }
        string? commandLine;
        try { commandLine = process.MainModule?.FileName; } catch { commandLine = null; }
        return new ProcessInfo { Pid = process.Id, ProcessName = process.ProcessName, CommandLine = commandLine, MemoryBytes = memory, StartTime = startTime };
    }

    private static void ValidateServiceName(string serviceName)
    {
        if (!ServiceNameRegex().IsMatch(serviceName))
        {
            throw new ArgumentException("Unsafe service name.", nameof(serviceName));
        }
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    [GeneratedRegex("^[A-Za-z0-9_.@-]+$")]
    private static partial Regex ServiceNameRegex();
}
