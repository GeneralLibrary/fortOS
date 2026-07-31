using System.Net.Http.Json;
using FortOS.Core;
using Microsoft.Extensions.Logging;

namespace FortOS.Observability.Logging;

/// <summary>Optional Loki Push API log store.</summary>
public sealed class LokiLogStore : ILogStore
{
    private readonly HttpClient _httpClient;
    private readonly string? _url;
    private readonly ILogger<LokiLogStore>? _logger;

    /// <summary>Initialize Loki log store.</summary>
    public LokiLogStore(IFortOSConfiguration? configuration = null, HttpClient? httpClient = null, ILogger<LokiLogStore>? logger = null)
    {
        _url = configuration?.GetValue("logging:loki:url");
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task AppendAsync(LogEntry entry, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_url))
        {
            _logger?.LogWarning("Loki not configured, skipping log push.");
            return;
        }

        try
        {
            var endpoint = new Uri(new Uri(_url.TrimEnd('/') + "/"), "loki/api/v1/push");
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
            using var response = await _httpClient.PostAsJsonAsync(endpoint, payload, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning("Loki push failed: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Loki unreachable, skipping log push.");
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
