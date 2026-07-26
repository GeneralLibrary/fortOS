using System.Diagnostics;
using System.Text.Json;
using GNAS.Core;
using Microsoft.Extensions.Logging;

namespace GNAS.ServiceBus.Hosts;

/// <summary>
/// Docker Compose container service host.
/// </summary>
public sealed class ContainerServiceHost : IServiceHost
{
    private readonly IProcessManager _processManager;
    private readonly IEventBus _eventBus;
    private readonly ILogger<ContainerServiceHost> _logger;
    private readonly ILogPipeline? _logPipeline;
    private readonly CancellationTokenSource _logsCts = new();
    private ServiceDefinition? _definition;
    private Task? _logsTask;

    /// <summary>
    /// Initialize the container service host.
    /// </summary>
    /// <param name="processManager">Process manager.</param>
    /// <param name="eventBus">Event bus.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="logPipeline">Optional log pipeline.</param>
    public ContainerServiceHost(IProcessManager processManager, IEventBus eventBus, ILogger<ContainerServiceHost> logger, ILogPipeline? logPipeline = null)
    {
        _processManager = processManager;
        _eventBus = eventBus;
        _logger = logger;
        _logPipeline = logPipeline;
    }

    /// <inheritdoc />
    public string ServiceId => _definition?.ServiceId ?? string.Empty;

    /// <inheritdoc />
    public async Task StartAsync(ServiceDefinition definition, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(definition.ComposeFile))
        {
            throw new ArgumentException("Container service must have a Compose file configured.", nameof(definition));
        }

        _definition = definition;
        await ExecuteComposeAsync(definition.ComposeFile, "up -d", ct).ConfigureAwait(false);
        _logsTask = Task.Run(() => FollowLogsAsync(definition.ServiceId, definition.ComposeFile, _logsCts.Token));
        await _eventBus.PublishAsync($"service.{definition.ServiceId}.started", "service.started", "{}", ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken ct)
    {
        if (_definition?.ComposeFile is null)
        {
            return;
        }

        _logsCts.Cancel();
        await ExecuteComposeAsync(_definition.ComposeFile, "down", ct).ConfigureAwait(false);
        await _eventBus.PublishAsync($"service.{_definition.ServiceId}.stopped", "service.stopped", "{}", ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ServiceStatusInfo> GetStatusAsync(CancellationToken ct)
    {
        if (_definition?.ComposeFile is null)
        {
            return new ServiceStatusInfo { ServiceId = string.Empty, Type = ServiceType.Container, Status = ServiceStatus.Unknown };
        }

        var result = await ExecuteComposeAsync(_definition.ComposeFile, "ps --format json", ct).ConfigureAwait(false);
        var status = ParseComposeStatus(result.Stdout);
        return new ServiceStatusInfo
        {
            ServiceId = _definition.ServiceId,
            Type = ServiceType.Container,
            Status = status,
        };
    }

    private Task<CommandResult> ExecuteComposeAsync(string composeFile, string arguments, CancellationToken ct)
        => _processManager.ExecuteCommandAsync(new ProcessStartConfig
        {
            ExecutablePath = "docker",
            Arguments = $"compose -f {Quote(composeFile)} {arguments}",
            TimeoutSeconds = 30,
        }, ct);

    private static ServiceStatus ParseComposeStatus(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return ServiceStatus.Stopped;
        }

        var any = false;
        var allRunning = true;
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                any = true;
                var root = document.RootElement;
                var state = root.TryGetProperty("State", out var stateProperty) ? stateProperty.GetString() : null;
                var status = root.TryGetProperty("Status", out var statusProperty) ? statusProperty.GetString() : null;
                if (!string.Equals(state, "running", StringComparison.OrdinalIgnoreCase)
                    && (status is null || !status.Contains("running", StringComparison.OrdinalIgnoreCase) && !status.Contains("up", StringComparison.OrdinalIgnoreCase)))
                {
                    allRunning = false;
                }
            }
            catch (JsonException)
            {
                allRunning = false;
            }
        }

        return !any ? ServiceStatus.Stopped : allRunning ? ServiceStatus.Running : ServiceStatus.Failed;
    }

    private async Task FollowLogsAsync(string serviceId, string composeFile, CancellationToken ct)
    {
        if (_logPipeline is null)
        {
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = $"compose -f {Quote(composeFile)} logs --follow --no-color",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return;
            }

            var stdout = ReadLinesAsync(process.StandardOutput, serviceId, ct);
            var stderr = ReadLinesAsync(process.StandardError, serviceId, ct);
            await Task.WhenAny(Task.WhenAll(stdout, stderr), process.WaitForExitAsync(ct)).ConfigureAwait(false);
            if (!ct.IsCancellationRequested && process.ExitCode != 0)
            {
                await _eventBus.PublishAsync($"service.{serviceId}.crashed", "service.crashed", "{}", ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Container log follow failed: {ServiceId}", serviceId);
        }
    }

    private async Task ReadLinesAsync(StreamReader reader, string serviceId, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (_logPipeline is not null)
            {
                await _logPipeline.ProcessRawAsync(line, LogCategory.System, serviceId, ct).ConfigureAwait(false);
            }
        }
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
