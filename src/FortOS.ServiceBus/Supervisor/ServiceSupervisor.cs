using System.Collections.Concurrent;
using FortOS.Core;
using FortOS.ServiceBus.Hosts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FortOS.ServiceBus.Supervisor;

/// <summary>
/// Service lifecycle supervisor.
/// </summary>
public sealed class ServiceSupervisor : BackgroundService, IServiceSupervisor
{
    private readonly IServiceRegistry _registry;
    private readonly IHealthMonitor _healthMonitor;
    private readonly IEventBus _eventBus;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ServiceSupervisor> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IServiceHost> _hosts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ServiceStatusInfo> _statuses = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _backoffAttempts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _startedAt = new(StringComparer.Ordinal);
    private readonly IDisposable _crashSubscription;

    /// <summary>
    /// Initialize the service supervisor.
    /// </summary>
    /// <param name="registry">Service registry.</param>
    /// <param name="healthMonitor">Health monitor.</param>
    /// <param name="eventBus">Event bus.</param>
    /// <param name="serviceProvider">Service provider.</param>
    /// <param name="logger">Logger.</param>
    public ServiceSupervisor(IServiceRegistry registry, IHealthMonitor healthMonitor, IEventBus eventBus, IServiceProvider serviceProvider, ILogger<ServiceSupervisor> logger)
    {
        _registry = registry;
        _healthMonitor = healthMonitor;
        _eventBus = eventBus;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _crashSubscription = _eventBus.Subscribe("service.*.crashed", OnCrashedAsync);
    }

    /// <inheritdoc />
    public async Task StartAsync(string serviceId, CancellationToken ct)
    {
        var gate = GetLock(serviceId);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_statuses.TryGetValue(serviceId, out var current) && current.Status == ServiceStatus.Running)
            {
                return;
            }

            var definition = await _registry.GetAsync(serviceId, ct).ConfigureAwait(false)
                ?? throw new ServiceNotFoundException($"Service does not exist: {serviceId}");
            await EnsureDependenciesAsync(definition, ct).ConfigureAwait(false);
            SetStatus(definition, ServiceStatus.Starting);
            await _eventBus.PublishAsync($"service.{serviceId}.starting", "service.starting", "{}", ct).ConfigureAwait(false);

            var host = ServiceHostFactory.Create(definition, _serviceProvider);
            if (definition.HealthCheck is not null)
            {
                await _healthMonitor.RegisterAsync(serviceId, definition.HealthCheck, ct).ConfigureAwait(false);
            }

            await host.StartAsync(definition, ct).ConfigureAwait(false);
            _hosts[serviceId] = host;
            _startedAt[serviceId] = DateTimeOffset.UtcNow;

            if (definition.HealthCheck is not null)
            {
                await WaitForHealthyAsync(serviceId, definition.HealthCheck, ct).ConfigureAwait(false);
            }

            SetStatus(definition, ServiceStatus.Running);
            await _eventBus.PublishAsync($"service.{serviceId}.started", "service.started", "{}", ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await SetFailedAsync(serviceId, ex.Message, ct).ConfigureAwait(false);
            throw;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(string serviceId, CancellationToken ct)
    {
        var gate = GetLock(serviceId);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var definition = await _registry.GetAsync(serviceId, ct).ConfigureAwait(false)
                ?? throw new ServiceNotFoundException($"Service does not exist: {serviceId}");
            SetStatus(definition, ServiceStatus.Stopping);
            if (_hosts.TryRemove(serviceId, out var host))
            {
                await host.StopAsync(ct).ConfigureAwait(false);
            }

            await _healthMonitor.UnregisterAsync(serviceId, ct).ConfigureAwait(false);
            SetStatus(definition, ServiceStatus.Stopped);
            await _eventBus.PublishAsync($"service.{serviceId}.stopped", "service.stopped", "{}", ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task RestartAsync(string serviceId, CancellationToken ct)
    {
        await StopAsync(serviceId, ct).ConfigureAwait(false);
        await StartAsync(serviceId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task StartAllAutomaticAsync(CancellationToken ct)
    {
        var definitions = (await _registry.ListAsync(ct).ConfigureAwait(false)).Where(s => s.Startup == ServiceStartup.Automatic).ToArray();
        foreach (var level in TopologySorter.SortLevels(definitions))
        {
            await Task.WhenAll(level.Select(service => StartAsync(service.ServiceId, ct))).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task ShutdownAllAsync(CancellationToken ct)
    {
        var definitions = await _registry.ListAsync(ct).ConfigureAwait(false);
        var levels = TopologySorter.SortLevels(definitions).Reverse();
        foreach (var level in levels)
        {
            await Task.WhenAll(level.Select(service => StopIfKnownAsync(service.ServiceId, ct))).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<ServiceStatusInfo> GetStatusAsync(string serviceId, CancellationToken ct)
    {
        if (_hosts.TryGetValue(serviceId, out var host))
        {
            var status = await host.GetStatusAsync(ct).ConfigureAwait(false);
            _statuses[serviceId] = status;
            return status;
        }

        if (_statuses.TryGetValue(serviceId, out var cached))
        {
            return cached;
        }

        var definition = await _registry.GetAsync(serviceId, ct).ConfigureAwait(false)
            ?? throw new ServiceNotFoundException($"Service not found: {serviceId}");
        var stopped = new ServiceStatusInfo { ServiceId = serviceId, Type = definition.Type, Status = ServiceStatus.Stopped };
        _statuses[serviceId] = stopped;
        return stopped;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ServiceStatusInfo>> ListStatusesAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ServiceStatusInfo>>(_statuses.Values.OrderBy(s => s.ServiceId, StringComparer.Ordinal).ToArray());

    /// <inheritdoc />
    public async Task RemoveAsync(string serviceId, CancellationToken ct)
    {
        var gate = GetLock(serviceId);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Stop the container/compose stack first, then release every in-memory resource
            // this supervisor holds for the service so it disappears from service listings.
            if (_hosts.TryRemove(serviceId, out var host))
            {
                try
                {
                    await host.StopAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error stopping container host while removing service {ServiceId}.", serviceId);
                }
            }

            _statuses.TryRemove(serviceId, out _);
            _startedAt.TryRemove(serviceId, out _);
            _backoffAttempts.TryRemove(serviceId, out _);
            await _healthMonitor.UnregisterAsync(serviceId, ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
            _locks.TryRemove(serviceId, out _);
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await ShutdownAllAsync(timeout.Token).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _crashSubscription.Dispose();
        foreach (var gate in _locks.Values)
        {
            gate.Dispose();
        }

        base.Dispose();
    }

    private async Task EnsureDependenciesAsync(ServiceDefinition definition, CancellationToken ct)
    {
        foreach (var dependencyId in definition.DependsOn)
        {
            var dependencyStatus = await GetStatusAsync(dependencyId, ct).ConfigureAwait(false);
            if (dependencyStatus.Status == ServiceStatus.Running)
            {
                continue;
            }

            var dependency = await _registry.GetAsync(dependencyId, ct).ConfigureAwait(false)
                ?? throw new ServiceNotFoundException($"Service does not exist: {dependencyId}");
            if (dependency.Startup != ServiceStartup.Automatic)
            {
                throw new InvalidOperationException($"Dependency service is not running and is not set to automatic start: {dependencyId}");
            }

            await StartAsync(dependencyId, ct).ConfigureAwait(false);
        }
    }

    private async Task WaitForHealthyAsync(string serviceId, HealthCheckConfig config, CancellationToken ct)
    {
        var timeout = TimeSpan.FromSeconds(Math.Max(1, config.StartPeriodSeconds + (config.IntervalSeconds * Math.Max(1, config.Retries))));
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await _healthMonitor.GetStatusAsync(serviceId, ct).ConfigureAwait(false) == HealthStatus.Healthy)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), ct).ConfigureAwait(false);
        }

        throw new TimeoutException($"Service health check did not become Healthy within the timeout period: {serviceId}");
    }

    private async Task OnCrashedAsync(EventEnvelope envelope, CancellationToken ct)
    {
        var serviceId = ExtractServiceId(envelope.Topic);
        if (string.IsNullOrWhiteSpace(serviceId))
        {
            return;
        }

        try
        {
            var definition = await _registry.GetAsync(serviceId, ct).ConfigureAwait(false)
                ?? throw new ServiceNotFoundException($"Service does not exist: {serviceId}");
            SetStatus(definition, ServiceStatus.Failed, "Service crashed.");
            if (definition.RestartPolicy == RestartPolicy.Never)
            {
                return;
            }

            var delay = GetRestartDelay(definition);
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }

            await RestartAsync(serviceId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process service crash: {Topic}", envelope.Topic);
        }
    }

    private TimeSpan GetRestartDelay(ServiceDefinition definition)
    {
        // Every restart policy (except Never, which the caller already filtered) goes through the
        // same exponential backoff: a crash-looping service must not hammer the host with
        // zero-delay restarts. The counter resets after 10 minutes of stability.
        if (_startedAt.TryGetValue(definition.ServiceId, out var start) && DateTimeOffset.UtcNow - start >= TimeSpan.FromMinutes(10))
        {
            _backoffAttempts[definition.ServiceId] = 0;
        }

        var attempt = _backoffAttempts.AddOrUpdate(definition.ServiceId, 1, (_, value) => value + 1);
        var seconds = Math.Min(60, 1 << Math.Min(5, attempt - 1));
        return TimeSpan.FromSeconds(seconds);
    }

    private async Task StopIfKnownAsync(string serviceId, CancellationToken ct)
    {
        if (_hosts.ContainsKey(serviceId) || _statuses.ContainsKey(serviceId))
        {
            await StopAsync(serviceId, ct).ConfigureAwait(false);
        }
    }

    private SemaphoreSlim GetLock(string serviceId) => _locks.GetOrAdd(serviceId, _ => new SemaphoreSlim(1, 1));

    private void SetStatus(ServiceDefinition definition, ServiceStatus status, string? error = null)
    {
        _statuses[definition.ServiceId] = new ServiceStatusInfo
        {
            ServiceId = definition.ServiceId,
            Type = definition.Type,
            Status = status,
            LastError = error,
        };
    }

    private async Task SetFailedAsync(string serviceId, string error, CancellationToken ct)
    {
        try
        {
            var definition = await _registry.GetAsync(serviceId, ct).ConfigureAwait(false);
            if (definition is not null)
            {
                SetStatus(definition, ServiceStatus.Failed, error);
            }
        }
        catch (ServiceNotFoundException)
        {
        }
    }

    private static string? ExtractServiceId(string topic)
    {
        var parts = topic.Split('.');
        return parts.Length >= 3 && parts[0] == "service" ? parts[1] : null;
    }
}
