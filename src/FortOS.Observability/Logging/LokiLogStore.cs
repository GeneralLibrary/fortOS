using System.Net.Http.Json;
using FortOS.Core;
using Microsoft.Extensions.Logging;

namespace FortOS.Observability.Logging;

/// <summary>Optional Loki Push API log store.</summary>
public sealed class LokiLogStore : ILogStore
{
    // This store is fed by the LogPipeline's DispatchStage. Any log line it emits would be
    // routed straight back into the pipeline and then into this store again, so a missing or
    // unreachable Loki would cause an unbounded self-feedback loop. Failures are therefore
    // reported at most once per process instead of once per call.
    private static int _reportedFailure;

    private readonly HttpClient _httpClient;
    private readonly string? _url;
    private readonly bool _enabled;
    private readonly ILogger<LokiLogStore>? _logger;

    /// <summary>Initialize Loki log store.</summary>
    public LokiLogStore(IFortOSConfiguration? configuration = null, HttpClient? httpClient = null, ILogger<LokiLogStore>? logger = null)
    {
        _url = configuration?.GetValue("logging:loki:url");
        _enabled = !string.IsNullOrWhiteSpace(_url);
        // Timeout is not set on the HttpClient: the TaskCanceledException thrown by the global Timeout cannot be
        // distinguished from an external cancellation, and once killed the log pipeline consumer by mistake (the whole
        // logging system deadlocked). Instead each push uses an independent timeout CTS (see AppendAsync), so external
        // cancellation and internal timeouts are cleanly separated.
        _httpClient = httpClient ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task AppendAsync(LogEntry entry, CancellationToken ct)
    {
        if (!_enabled)
        {
            return; // Not configured: silent no-op (never log here, see class doc).
        }

        try
        {
            var endpoint = new Uri(new Uri(_url!.TrimEnd('/') + "/"), "loki/api/v1/push");
            var labels = new Dictionary<string, string>
            {
                ["category"] = entry.Category.ToString().ToLowerInvariant(),
                ["service_id"] = entry.ServiceId ?? string.Empty,
                ["agent_id"] = entry.AgentId ?? string.Empty
            };
            var payload = new
            {
                streams = new[]
                {
                    new
                    {
                        stream = labels,
                        values = new[] { new[] { (entry.Timestamp.ToUnixTimeMilliseconds() * 1_000_000).ToString(), entry.Message } }
                    }
                }
            };
            // Independent timeout: when Loki hangs, only this push is canceled; it never propagates to the pipeline consumer.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));
            using var response = await _httpClient.PostAsJsonAsync(endpoint, payload, timeoutCts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                ReportFailureOnce($"Loki push failed: {response.StatusCode}");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Catch internal timeouts and network errors (reported once); an external cancellation (ct already requested) keeps propagating upward.
            ReportFailureOnce("Loki unreachable, skipping log push.", ex);
        }
    }

    private void ReportFailureOnce(string message, Exception? ex = null)
    {
        if (Interlocked.Exchange(ref _reportedFailure, 1) == 0)
        {
            _logger?.LogWarning(ex, "{Message}", message);
        }
    }

    /// <inheritdoc />
    public async Task AppendBatchAsync(IEnumerable<LogEntry> entries, CancellationToken ct)
    {
        foreach (var entry in entries)
        {
            await AppendAsync(entry, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<LogEntry>> QueryAsync(LogQuery query, CancellationToken ct)
        => throw new NotSupportedException("LokiLogStore does not support local QueryAsync; please query logs via Loki's own query API.");
}
