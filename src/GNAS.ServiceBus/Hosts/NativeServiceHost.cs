using GNAS.Core;
using Microsoft.Extensions.Logging;

namespace GNAS.ServiceBus.Hosts;

/// <summary>
/// 原生进程服务宿主。
/// </summary>
public sealed class NativeServiceHost : IServiceHost
{
    private readonly IProcessManager _processManager;
    private readonly IEventBus _eventBus;
    private readonly ILogger<NativeServiceHost> _logger;
    private readonly ILogPipeline? _logPipeline;
    private readonly CancellationTokenSource _monitorCts = new();
    private ProcessInfo? _process;
    private ServiceDefinition? _definition;
    private Task? _monitorTask;
    private bool _stopping;

    /// <summary>
    /// 初始化原生进程服务宿主。
    /// </summary>
    /// <param name="processManager">进程管理器。</param>
    /// <param name="eventBus">事件总线。</param>
    /// <param name="logger">日志记录器。</param>
    /// <param name="logPipeline">可选日志管线。</param>
    public NativeServiceHost(IProcessManager processManager, IEventBus eventBus, ILogger<NativeServiceHost> logger, ILogPipeline? logPipeline = null)
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
        if (string.IsNullOrWhiteSpace(definition.Executable))
        {
            throw new ArgumentException("原生服务必须配置可执行文件。", nameof(definition));
        }

        _definition = definition;
        _stopping = false;
        _process = await _processManager.StartProcessAsync(new ProcessStartConfig
        {
            ExecutablePath = definition.Executable,
            Arguments = definition.Arguments,
        }, ct).ConfigureAwait(false);
        _monitorTask = Task.Run(() => MonitorExitAsync(definition.ServiceId, _process.Pid, _monitorCts.Token));
        await _eventBus.PublishAsync($"service.{definition.ServiceId}.started", "service.started", "{}", ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken ct)
    {
        _stopping = true;
        if (_process is not null)
        {
            await _processManager.StopProcessAsync(_process.Pid, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            await _eventBus.PublishAsync($"service.{ServiceId}.stopped", "service.stopped", "{}", ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<ServiceStatusInfo> GetStatusAsync(CancellationToken ct)
    {
        if (_definition is null)
        {
            return new ServiceStatusInfo { ServiceId = string.Empty, Type = ServiceType.Native, Status = ServiceStatus.Unknown };
        }

        var process = _process is null ? null : await _processManager.GetProcessAsync(_process.Pid, ct).ConfigureAwait(false);
        return new ServiceStatusInfo
        {
            ServiceId = _definition.ServiceId,
            Type = ServiceType.Native,
            Status = process is null ? ServiceStatus.Stopped : ServiceStatus.Running,
            Pid = process?.Pid,
            CpuPercent = process?.CpuPercent ?? 0,
            MemoryBytes = process?.MemoryBytes ?? 0,
            Uptime = process is null ? TimeSpan.Zero : DateTimeOffset.UtcNow - process.StartTime,
        };
    }

    private async Task MonitorExitAsync(string serviceId, int pid, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
                var process = await _processManager.GetProcessAsync(pid, ct).ConfigureAwait(false);
                if (process is not null)
                {
                    continue;
                }

                var topic = _stopping ? $"service.{serviceId}.stopped" : $"service.{serviceId}.crashed";
                var type = _stopping ? "service.stopped" : "service.crashed";
                await _eventBus.PublishAsync(topic, type, "{}", ct).ConfigureAwait(false);
                if (_logPipeline is not null)
                {
                    await _logPipeline.ProcessRawAsync($"服务 {serviceId} 进程已退出。", LogCategory.System, serviceId, ct).ConfigureAwait(false);
                }

                return;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "监控原生服务退出失败: {ServiceId}", serviceId);
        }
    }
}
