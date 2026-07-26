using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;
using GNAS.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GNAS.ServiceBus.Health;

/// <summary>
/// Periodic service health monitor.
/// </summary>
public sealed class HealthMonitor : BackgroundService, IHealthMonitor
{
    private static readonly HttpClient HttpClient = new();
    private readonly ConcurrentDictionary<string, HealthState> _states = new(StringComparer.Ordinal);
    private readonly IProcessManager _processManager;
    private readonly IEventBus _eventBus;
    private readonly ILogger<HealthMonitor> _logger;

    /// <summary>
    /// Initialize the health monitor.
    /// </summary>
    /// <param name="processManager">Process manager.</param>
    /// <param name="eventBus">Event bus.</param>
    /// <param name="logger">Logger.</param>
    public HealthMonitor(IProcessManager processManager, IEventBus eventBus, ILogger<HealthMonitor> logger)
    {
        _processManager = processManager;
        _eventBus = eventBus;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task RegisterAsync(string serviceId, HealthCheckConfig config, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);
        ArgumentNullException.ThrowIfNull(config);
        _states[serviceId] = new HealthState(config, DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UnregisterAsync(string serviceId, CancellationToken ct)
    {
        _states.TryRemove(serviceId, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<HealthStatus> GetStatusAsync(string serviceId, CancellationToken ct)
        => Task.FromResult(_states.TryGetValue(serviceId, out var state) ? state.Status : HealthStatus.Unknown);

    /// <inheritdoc />
    public Task<IReadOnlyList<HealthCheckResult>> GetRecentResultsAsync(string serviceId, int limit, CancellationToken ct)
    {
        if (!_states.TryGetValue(serviceId, out var state))
        {
            return Task.FromResult<IReadOnlyList<HealthCheckResult>>(Array.Empty<HealthCheckResult>());
        }

        lock (state.SyncRoot)
        {
            return Task.FromResult<IReadOnlyList<HealthCheckResult>>(state.Results.Reverse().Take(Math.Max(0, limit)).ToArray());
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<double, TimeSpan>> GetLatencyPercentilesAsync(string serviceId, IReadOnlyList<double> percentiles, CancellationToken ct)
    {
        if (!_states.TryGetValue(serviceId, out var state))
        {
            return Task.FromResult<IReadOnlyDictionary<double, TimeSpan>>(new Dictionary<double, TimeSpan>());
        }

        TimeSpan[] samples;
        lock (state.SyncRoot)
        {
            samples = state.Results.Select(r => r.ResponseTime).Order().ToArray();
        }

        var result = new Dictionary<double, TimeSpan>();
        foreach (var percentile in percentiles)
        {
            result[percentile] = Percentile(samples, percentile);
        }

        return Task.FromResult<IReadOnlyDictionary<double, TimeSpan>>(result);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            var tasks = _states.Select(pair => CheckIfDueAsync(pair.Key, pair.Value, now, stoppingToken)).ToArray();
            await Task.WhenAll(tasks).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task CheckIfDueAsync(string serviceId, HealthState state, DateTimeOffset now, CancellationToken ct)
    {
        if (now - state.RegisteredAt < TimeSpan.FromSeconds(Math.Max(0, state.Config.StartPeriodSeconds)))
        {
            return;
        }

        if (now < state.NextCheckAt)
        {
            return;
        }

        state.NextCheckAt = now + TimeSpan.FromSeconds(Math.Max(1, state.Config.IntervalSeconds));
        var result = await RunCheckAsync(serviceId, state, ct).ConfigureAwait(false);
        HealthStatus? transition = null;
        lock (state.SyncRoot)
        {
            state.Results.Enqueue(result);
            while (state.Results.Count > 100)
            {
                state.Results.Dequeue();
            }

            var previous = state.Status;
            state.Status = ComputeStatus(previous, result.ConsecutiveFailures, result.ConsecutiveSuccesses, Math.Max(1, state.Config.Retries));
            if (state.Status != previous)
            {
                transition = state.Status;
            }
        }

        if (transition.HasValue)
        {
            var suffix = transition.Value.ToString().ToLowerInvariant();
            await _eventBus.PublishAsync($"service.{serviceId}.health.{suffix}", $"service.health.{suffix}", "{}", ct).ConfigureAwait(false);
        }
    }

    private async Task<HealthCheckResult> RunCheckAsync(string serviceId, HealthState state, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        string? error = null;
        var healthy = false;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, state.Config.TimeoutSeconds)));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            healthy = state.Config.Type switch
            {
                HealthCheckType.HttpGet => await CheckHttpAsync(state.Config.Endpoint, linked.Token).ConfigureAwait(false),
                HealthCheckType.TcpConnect => await CheckTcpAsync(state.Config.Endpoint, linked.Token).ConfigureAwait(false),
                HealthCheckType.ExecCommand => await CheckCommandAsync(state.Config.Endpoint, state.Config.TimeoutSeconds, linked.Token).ConfigureAwait(false),
                HealthCheckType.Grpc => await CheckGrpcAsync(state.Config.Endpoint, linked.Token).ConfigureAwait(false),
                _ => false,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            error = ex.Message;
            healthy = false;
        }
        finally
        {
            stopwatch.Stop();
        }

        if (healthy)
        {
            state.ConsecutiveSuccesses++;
            state.ConsecutiveFailures = 0;
        }
        else
        {
            state.ConsecutiveFailures++;
            state.ConsecutiveSuccesses = 0;
        }

        return new HealthCheckResult
        {
            ServiceId = serviceId,
            Status = healthy ? HealthStatus.Healthy : state.Status,
            ResponseTime = stopwatch.Elapsed,
            ErrorMessage = healthy ? null : error ?? "Health check failed.",
            ConsecutiveFailures = state.ConsecutiveFailures,
            ConsecutiveSuccesses = state.ConsecutiveSuccesses,
        };
    }

    private static async Task<bool> CheckHttpAsync(string endpoint, CancellationToken ct)
    {
        using var response = await HttpClient.GetAsync(endpoint, ct).ConfigureAwait(false);
        return ((int)response.StatusCode >= 200) && ((int)response.StatusCode <= 299);
    }

    private static async Task<bool> CheckTcpAsync(string endpoint, CancellationToken ct)
    {
        var (host, port) = ParseEndpoint(endpoint);
        using var client = new TcpClient();
        await client.ConnectAsync(host, port, ct).ConfigureAwait(false);
        return client.Connected;
    }

    private async Task<bool> CheckCommandAsync(string endpoint, int timeoutSeconds, CancellationToken ct)
    {
        var (fileName, arguments) = SplitCommand(endpoint);
        var result = await _processManager.ExecuteCommandAsync(new ProcessStartConfig
        {
            ExecutablePath = fileName,
            Arguments = arguments,
            TimeoutSeconds = Math.Max(1, timeoutSeconds),
        }, ct).ConfigureAwait(false);
        return result.ExitCode == 0;
    }

    private async Task<bool> CheckGrpcAsync(string endpoint, CancellationToken ct)
    {
        _logger.LogDebug("gRPC health check uses TCP reachability as a simplified implementation: {Endpoint}", endpoint);
        return await CheckTcpAsync(endpoint, ct).ConfigureAwait(false);
    }

    private static HealthStatus ComputeStatus(HealthStatus previous, int failures, int successes, int retries)
    {
        if (successes > 0)
        {
            return successes >= retries ? HealthStatus.Healthy : previous switch
            {
                HealthStatus.Unhealthy => HealthStatus.Degraded,
                HealthStatus.Unknown => HealthStatus.Healthy,
                _ => previous,
            };
        }

        return previous switch
        {
            HealthStatus.Healthy or HealthStatus.Unknown when failures >= retries => HealthStatus.Degraded,
            HealthStatus.Degraded when failures >= retries * 2 => HealthStatus.Unhealthy,
            _ => previous,
        };
    }

    private static TimeSpan Percentile(TimeSpan[] sortedSamples, double percentile)
    {
        if (sortedSamples.Length == 0)
        {
            return TimeSpan.Zero;
        }

        var normalized = percentile > 1 ? percentile / 100d : percentile;
        normalized = Math.Clamp(normalized, 0, 1);
        var index = (int)Math.Ceiling(normalized * sortedSamples.Length) - 1;
        return sortedSamples[Math.Clamp(index, 0, sortedSamples.Length - 1)];
    }

    private static (string Host, int Port) ParseEndpoint(string endpoint)
    {
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Port > 0)
        {
            return (uri.Host, uri.Port);
        }

        var parts = endpoint.Split(':', 2);
        if (parts.Length != 2 || !int.TryParse(parts[1], out var port))
        {
            throw new FormatException($"Invalid TCP endpoint format: {endpoint}");
        }

        return (parts[0], port);
    }

    private static (string FileName, string? Arguments) SplitCommand(string command)
    {
        var trimmed = command.Trim();
        var index = trimmed.IndexOf(' ', StringComparison.Ordinal);
        return index < 0 ? (trimmed, null) : (trimmed[..index], trimmed[(index + 1)..]);
    }

    private sealed class HealthState
    {
        public HealthState(HealthCheckConfig config, DateTimeOffset registeredAt)
        {
            Config = config;
            RegisteredAt = registeredAt;
            NextCheckAt = registeredAt;
        }

        public object SyncRoot { get; } = new();
        public HealthCheckConfig Config { get; }
        public DateTimeOffset RegisteredAt { get; }
        public DateTimeOffset NextCheckAt { get; set; }
        public HealthStatus Status { get; set; } = HealthStatus.Unknown;
        public int ConsecutiveFailures { get; set; }
        public int ConsecutiveSuccesses { get; set; }
        public Queue<HealthCheckResult> Results { get; } = new();
    }
}
