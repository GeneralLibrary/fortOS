using System.Globalization;
using System.Text.RegularExpressions;
using GNAS.Core;

namespace GNAS.ServiceBus.Hosts;

/// <summary>
/// 通过 systemd 管理发行版提供的系统服务，避免另起守护进程与系统单元争用端口。
/// </summary>
public sealed partial class SystemdServiceHost : IServiceHost
{
    private readonly IProcessManager _processManager;
    private readonly IEventBus _eventBus;
    private ServiceDefinition? _definition;

    /// <summary>初始化 systemd 服务宿主。</summary>
    public SystemdServiceHost(IProcessManager processManager, IEventBus eventBus)
    {
        _processManager = processManager;
        _eventBus = eventBus;
    }

    /// <inheritdoc />
    public string ServiceId => _definition?.ServiceId ?? string.Empty;

    /// <inheritdoc />
    public async Task StartAsync(ServiceDefinition definition, CancellationToken ct)
    {
        var unit = GetValidatedUnit(definition);
        _definition = definition;
        var result = await ExecuteSystemctlAsync($"start {Quote(unit)}", ct).ConfigureAwait(false);
        EnsureSuccess(result, unit, "启动");
        await _eventBus.PublishAsync(
            $"service.{definition.ServiceId}.started",
            "service.started",
            "{}",
            ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken ct)
    {
        if (_definition is null)
        {
            return;
        }

        var unit = GetValidatedUnit(_definition);
        var result = await ExecuteSystemctlAsync($"stop {Quote(unit)}", ct).ConfigureAwait(false);
        EnsureSuccess(result, unit, "停止");
        await _eventBus.PublishAsync(
            $"service.{_definition.ServiceId}.stopped",
            "service.stopped",
            "{}",
            ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ServiceStatusInfo> GetStatusAsync(CancellationToken ct)
    {
        if (_definition is null)
        {
            return new ServiceStatusInfo
            {
                ServiceId = string.Empty,
                Type = ServiceType.Systemd,
                Status = ServiceStatus.Unknown,
            };
        }

        var unit = GetValidatedUnit(_definition);
        var result = await _processManager.ExecuteCommandAsync(new ProcessStartConfig
        {
            ExecutablePath = "systemctl",
            Arguments = $"show {Quote(unit)} --property=ActiveState,MainPID,MemoryCurrent",
            TimeoutSeconds = 15,
        }, ct).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return new ServiceStatusInfo
            {
                ServiceId = _definition.ServiceId,
                Type = ServiceType.Systemd,
                Status = ServiceStatus.Failed,
                LastError = result.Stderr,
            };
        }

        var properties = ParseProperties(result.Stdout);
        var status = properties.GetValueOrDefault("ActiveState") switch
        {
            "active" => ServiceStatus.Running,
            "activating" => ServiceStatus.Starting,
            "deactivating" => ServiceStatus.Stopping,
            "failed" => ServiceStatus.Failed,
            "inactive" => ServiceStatus.Stopped,
            _ => ServiceStatus.Unknown,
        };
        _ = int.TryParse(properties.GetValueOrDefault("MainPID"), NumberStyles.None, CultureInfo.InvariantCulture, out var pid);
        _ = long.TryParse(properties.GetValueOrDefault("MemoryCurrent"), NumberStyles.None, CultureInfo.InvariantCulture, out var memory);

        return new ServiceStatusInfo
        {
            ServiceId = _definition.ServiceId,
            Type = ServiceType.Systemd,
            Status = status,
            Pid = pid > 0 ? pid : null,
            MemoryBytes = memory > 0 ? memory : 0,
        };
    }

    private Task<CommandResult> ExecuteSystemctlAsync(string arguments, CancellationToken ct)
        => _processManager.ExecuteCommandAsync(new ProcessStartConfig
        {
            ExecutablePath = "systemctl",
            Arguments = arguments,
            TimeoutSeconds = 30,
        }, ct);

    private static void EnsureSuccess(CommandResult result, string unit, string operation)
    {
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"systemd 单元 {unit} {operation}失败：{result.Stderr}");
        }
    }

    private static string GetValidatedUnit(ServiceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(definition.SystemdUnit)
            || !SystemdUnitRegex().IsMatch(definition.SystemdUnit))
        {
            throw new ArgumentException("systemd 服务必须配置安全的单元名称。", nameof(definition));
        }

        return definition.SystemdUnit;
    }

    private static Dictionary<string, string> ParseProperties(string output)
        => output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);

    private static string Quote(string value) => "\"" + value + "\"";

    [GeneratedRegex("^[A-Za-z0-9_.@-]+\\.service$")]
    private static partial Regex SystemdUnitRegex();
}
