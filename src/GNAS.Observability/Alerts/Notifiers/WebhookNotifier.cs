using System.Net.Http.Json;
using GNAS.Core;
using Microsoft.Extensions.Logging;

namespace GNAS.Observability.Alerts.Notifiers;

/// <summary>Alert notifier based on HTTP Webhook.</summary>
public sealed class WebhookNotifier : INotifier
{
    private readonly IGnasConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly ILogger<WebhookNotifier>? _logger;

    /// <summary>Initialize Webhook notifier.</summary>
    public WebhookNotifier(IGnasConfiguration configuration, HttpClient? httpClient = null, ILogger<WebhookNotifier>? logger = null)
    {
        _configuration = configuration;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task NotifyAsync(ActiveAlert alert, AlertRule rule, CancellationToken ct)
    {
        var urls = _configuration.GetArray("alerts:webhook:urls");
        if (urls.Length == 0)
        {
            _logger?.LogWarning("Webhook not configured, skipping alert push.");
            return;
        }

        foreach (var url in urls.Where(url => Uri.TryCreate(url, UriKind.Absolute, out _)))
        {
            try
            {
                await _httpClient.PostAsJsonAsync(url, new { alert, rule }, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogWarning(ex, "Webhook alert push failed.");
            }
        }
    }
}
