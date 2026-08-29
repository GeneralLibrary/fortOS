using System.Text.Json;
using System.Text.RegularExpressions;
using FortOS.Core;
using FortOS.Observability.Alerts.Notifiers;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace FortOS.Observability.Alerts;

/// <summary>Alert engine and event subscription background service.</summary>
public sealed class AlertEngine : IAlertEngine, IHostedService, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IFortOSConfiguration _configuration;
    private readonly IDatabaseProvider _database;
    private readonly IEventBus _eventBus;
    private readonly IReadOnlyList<INotifier> _notifiers;
    private readonly ILogger<AlertEngine> _logger;
    private readonly object _sync = new();
    private readonly List<AlertRule> _rules = [];
    private readonly List<ActiveAlert> _activeAlerts = [];
    private readonly Dictionary<string, DateTimeOffset> _lastFired = [];
    private readonly Dictionary<string, List<DateTimeOffset>> _eventWindows = [];
    private readonly Dictionary<string, DateTimeOffset> _eventLastMatched = [];
    private readonly Dictionary<string, DateTimeOffset> _metricSince = [];
    private readonly Dictionary<string, HashSet<INotifier>> _pendingActiveNotifications = [];
    private readonly CancellationTokenSource _stopping = new();
    private IDisposable? _subscription;

    /// <summary>Initialize the alert engine.</summary>
    public AlertEngine(
        IFortOSConfiguration configuration,
        IDatabaseProvider database,
        IEventBus eventBus,
        IEnumerable<INotifier> notifiers,
        ILogger<AlertEngine>? logger = null)
    {
        _configuration = configuration;
        _database = database;
        _eventBus = eventBus;
        _notifiers = notifiers.ToArray();
        _logger = logger ?? NullLogger<AlertEngine>.Instance;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await LoadRulesAsync(cancellationToken).ConfigureAwait(false);
        _subscription = _eventBus.Subscribe("**", EvaluateEventAsync);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        _stopping.Cancel();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task LoadRulesAsync(CancellationToken ct)
    {
        var loaded = new List<AlertRule>(CreateDefaultSloRules());
        loaded.AddRange(await LoadYamlRulesAsync(ct).ConfigureAwait(false));
        loaded.AddRange(await LoadDatabaseRulesAsync(ct).ConfigureAwait(false));
        lock (_sync)
        {
            _rules.Clear();
            _rules.AddRange(loaded.GroupBy(rule => rule.RuleId).Select(group => group.Last()));
        }
    }

    /// <inheritdoc />
    public async Task EvaluateEventAsync(EventEnvelope envelope, CancellationToken ct)
    {
        foreach (var rule in SnapshotRules().Where(rule => rule.Condition.Type.Equals("event", StringComparison.OrdinalIgnoreCase)))
        {
            if (!TopicMatches(rule.Condition.Topic, envelope.Topic) && !TopicMatches(rule.Condition.Topic, envelope.Type)) continue;
            var now = DateTimeOffset.UtcNow;
            var dimensions = new Dictionary<string, string>(StringComparer.Ordinal) { ["topic"] = envelope.Topic };
            var instanceKey = BuildMetricInstanceKey(rule.RuleId, dimensions);
            lock (_sync) _eventLastMatched[instanceKey] = now;
            if (rule.Condition.WithinSeconds is { } windowSeconds && rule.Condition.Count is { } count)
            {
                List<DateTimeOffset> bucket;
                lock (_sync)
                {
                    bucket = _eventWindows.TryGetValue(instanceKey, out var existing) ? existing : (_eventWindows[instanceKey] = []);
                    bucket.Add(now);
                    bucket.RemoveAll(item => item < now.AddSeconds(-windowSeconds));
                    if (bucket.Count < count) continue;
                }
            }

            await FireAsync(rule, instanceKey, $"Event rule {rule.Name} triggered: {envelope.Topic}", dimensions, ct).ConfigureAwait(false);
            if (rule.Condition.WithinSeconds is { } quietSeconds and > 0)
            {
                _ = ResolveEventAfterQuietPeriodAsync(rule, instanceKey, dimensions, now, quietSeconds, _stopping.Token);
            }
        }
    }

    /// <inheritdoc />
    public async Task EvaluateMetricAsync(MetricData metric, CancellationToken ct)
    {
        foreach (var rule in SnapshotRules().Where(rule => rule.Condition.Type.Equals("metric", StringComparison.OrdinalIgnoreCase)))
        {
            if (!string.Equals(rule.Condition.Metric, metric.MetricName, StringComparison.OrdinalIgnoreCase)) continue;
            var instanceKey = BuildMetricInstanceKey(rule.RuleId, metric.Dimensions);
            if (!Compare(metric.Value, rule.Condition.Operator, rule.Condition.Value))
            {
                lock (_sync) _metricSince.Remove(instanceKey);
                await ResolveAsync(rule, metric, instanceKey, ct).ConfigureAwait(false);
                continue;
            }

            if (rule.Condition.DurationSeconds is { } durationSeconds and > 0)
            {
                var now = DateTimeOffset.UtcNow;
                lock (_sync)
                {
                    if (!_metricSince.TryGetValue(instanceKey, out var since))
                    {
                        _metricSince[instanceKey] = now;
                        continue;
                    }

                    if (now - since < TimeSpan.FromSeconds(durationSeconds)) continue;
                }
            }

            await FireAsync(
                rule,
                instanceKey,
                $"Metric rule {rule.Name} triggered: {metric.MetricName}={metric.Value}{FormatDimensions(metric.Dimensions)}",
                metric.Dimensions,
                ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ActiveAlert>> ListActiveAlertsAsync(CancellationToken ct)
    {
        lock (_sync) return Task.FromResult<IReadOnlyList<ActiveAlert>>(_activeAlerts.ToArray());
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AlertRule>> ListRulesAsync(CancellationToken ct)
    {
        lock (_sync) return Task.FromResult<IReadOnlyList<AlertRule>>(_rules.ToArray());
    }

    /// <inheritdoc />
    public async Task AddRuleAsync(AlertRule rule, CancellationToken ct)
    {
        await _database.InitializeAsync(ct).ConfigureAwait(false);
        await using var connection = await _database.GetConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR REPLACE INTO alert_rules (rule_id, rule_json, enabled, updated_at) VALUES ($id, $json, 1, $updated);";
        command.Parameters.AddWithValue("$id", rule.RuleId);
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(rule, JsonOptions));
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        lock (_sync)
        {
            _rules.RemoveAll(existing => existing.RuleId == rule.RuleId);
            _rules.Add(rule);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _subscription?.Dispose();
        // Cancel 后立即 Dispose 的 CTS,若 Dispose 被重复调用(宿主 Stop + 容器释放都会触发),
        // 第二次 Cancel 会对已处置的 CTS 抛 ObjectDisposedException —— 必须容错,否则进程 ABRT。
        try
        {
            _stopping.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 已被处置(重复 Dispose),忽略。
        }

        _stopping.Dispose();
    }

    private async Task<IReadOnlyList<AlertRule>> LoadYamlRulesAsync(CancellationToken ct)
    {
        var path = Path.Combine(GetConfigDirectory(), "alerts.yaml");
        if (!File.Exists(path)) return [];
        var yaml = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        try
        {
            var wrapper = deserializer.Deserialize<AlertRulesDocument>(yaml);
            if (wrapper?.Rules is { Count: > 0 }) return wrapper.Rules;
        }
        catch (YamlDotNet.Core.YamlException)
        {
        }

        try
        {
            return deserializer.Deserialize<List<AlertRule>>(yaml) ?? [];
        }
        catch (YamlDotNet.Core.YamlException)
        {
            return [];
        }
    }

    private async Task<IReadOnlyList<AlertRule>> LoadDatabaseRulesAsync(CancellationToken ct)
    {
        await _database.InitializeAsync(ct).ConfigureAwait(false);
        await using var connection = await _database.GetConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT rule_json FROM alert_rules WHERE enabled = 1;";
        var rules = new List<AlertRule>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var rule = JsonSerializer.Deserialize<AlertRule>(reader.GetString(0), JsonOptions);
            if (rule is not null) rules.Add(rule);
        }
        return rules;
    }

    private async Task FireAsync(
        AlertRule rule,
        string instanceKey,
        string message,
        IReadOnlyDictionary<string, string>? dimensions,
        CancellationToken ct)
    {
        if (IsSuppressed(rule)) return;
        var now = DateTimeOffset.UtcNow;
        var notifiers = SelectNotifiers(rule).ToArray();
        ActiveAlert alert;
        lock (_sync)
        {
            var existing = _activeAlerts.FirstOrDefault(candidate =>
                BuildMetricInstanceKey(candidate.RuleId, candidate.Dimensions) == instanceKey);
            if (existing is not null)
            {
                if (!_pendingActiveNotifications.TryGetValue(instanceKey, out var pending) || pending.Count == 0) return;
                // Retry failed deliveries no more often than the rule's cooldown (floored at 30s)
                // so a broken notifier (e.g. an unreachable mail server) is not hammered on every
                // sampling tick. Rules that omit cooldown_seconds default to 0, which is
                // indistinguishable from "forgot to configure it" — hence the floor.
                var retryAfter = Math.Max(rule.CooldownSeconds, 30);
                if (_lastFired.TryGetValue(instanceKey, out var last) && now - last < TimeSpan.FromSeconds(retryAfter)) return;
                _lastFired[instanceKey] = now;
                alert = existing;
            }
            else
            {
                if (_lastFired.TryGetValue(instanceKey, out var last)
                    && now - last < TimeSpan.FromSeconds(rule.CooldownSeconds)) return;
                _lastFired[instanceKey] = now;
                alert = new ActiveAlert
                {
                    AlertId = Guid.CreateVersion7().ToString(),
                    RuleId = rule.RuleId,
                    Severity = rule.Severity,
                    Message = message,
                    TriggeredAt = now,
                    Dimensions = dimensions is null
                        ? []
                        : new Dictionary<string, string>(dimensions, StringComparer.Ordinal),
                };
                _activeAlerts.Add(alert);
                _pendingActiveNotifications[instanceKey] = notifiers.ToHashSet();
            }
        }

        await DeliverActiveNotificationsAsync(instanceKey, alert, rule, ct).ConfigureAwait(false);
    }

    private async Task ResolveAsync(
        AlertRule rule,
        MetricData metric,
        string instanceKey,
        CancellationToken ct,
        DateTimeOffset? eventMatchedAt = null)
    {
        ActiveAlert[] resolved;
        lock (_sync)
        {
            if (eventMatchedAt is { } expected
                && (!_eventLastMatched.TryGetValue(instanceKey, out var latest) || latest != expected)) return;
            resolved = _activeAlerts
                .Where(alert => BuildMetricInstanceKey(alert.RuleId, alert.Dimensions) == instanceKey)
                .ToArray();
            if (resolved.Length == 0) return;
            _activeAlerts.RemoveAll(alert => BuildMetricInstanceKey(alert.RuleId, alert.Dimensions) == instanceKey);
            _lastFired.Remove(instanceKey);
            _pendingActiveNotifications.Remove(instanceKey);
        }

        foreach (var alert in resolved)
        {
            foreach (var notifier in SelectNotifiers(rule))
            {
                try
                {
                    await notifier.NotifyResolvedAsync(alert, rule, metric, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Alert recovery notification failed for rule {RuleId} via {Notifier}.", rule.RuleId, notifier.GetType().Name);
                }
            }
        }
        await _eventBus.PublishAsync(
            "alert.resolved",
            "observability.alert.resolved",
            JsonSerializer.Serialize(new
            {
                rule.RuleId,
                metric.MetricName,
                metric.Value,
                alertIds = resolved.Select(alert => alert.AlertId).ToArray(),
                resolvedAt = DateTimeOffset.UtcNow,
            }, JsonOptions),
            ct).ConfigureAwait(false);
    }

    private async Task DeliverActiveNotificationsAsync(
        string instanceKey,
        ActiveAlert alert,
        AlertRule rule,
        CancellationToken ct)
    {
        INotifier[] pending;
        lock (_sync)
        {
            pending = _pendingActiveNotifications.TryGetValue(instanceKey, out var channels)
                ? channels.ToArray()
                : [];
        }

        foreach (var notifier in pending)
        {
            try
            {
                await notifier.NotifyAsync(alert, rule, ct).ConfigureAwait(false);
                lock (_sync)
                {
                    if (_pendingActiveNotifications.TryGetValue(instanceKey, out var channels))
                    {
                        channels.Remove(notifier);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Alert notification failed for rule {RuleId} via {Notifier}; delivery will be retried.", rule.RuleId, notifier.GetType().Name);
            }
        }
    }

    private async Task ResolveEventAfterQuietPeriodAsync(
        AlertRule rule,
        string instanceKey,
        IReadOnlyDictionary<string, string> dimensions,
        DateTimeOffset matchedAt,
        int quietSeconds,
        CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(quietSeconds), ct).ConfigureAwait(false);
            await ResolveAsync(
                rule,
                new MetricData
                {
                    MetricName = "event.quiet_window",
                    Unit = "seconds",
                    Value = quietSeconds,
                    Timestamp = DateTimeOffset.UtcNow,
                    Dimensions = new Dictionary<string, string>(dimensions, StringComparer.Ordinal),
                },
                instanceKey,
                ct,
                matchedAt).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve event alert {RuleId} after its quiet period.", rule.RuleId);
        }
    }

    private IEnumerable<INotifier> SelectNotifiers(AlertRule rule)
    {
        if (rule.Actions.Length > 0)
        {
            var actions = rule.Actions.ToHashSet(StringComparer.OrdinalIgnoreCase);
            return _notifiers.Where(notifier => notifier switch
            {
                EmailNotifier => actions.Contains("email"),
                WebhookNotifier => actions.Contains("webhook"),
                SystemNotifier => actions.Contains("system") || actions.Contains("log"),
                _ => true,
            });
        }

        var lower = rule.Severity.ToLowerInvariant();
        return _notifiers.Where(notifier => notifier switch
        {
            EmailNotifier => lower is "critical" or "warning",
            WebhookNotifier => lower is "critical",
            SystemNotifier => true,
            _ => true
        });
    }

    private bool IsSuppressed(AlertRule rule)
    {
        var critical = rule.Severity.Equals("critical", StringComparison.OrdinalIgnoreCase);
        if (!critical && bool.TryParse(_configuration.GetValue("alerts:maintenance"), out var maintenance) && maintenance) return true;
        var window = rule.Suppress?.Window;
        if (string.IsNullOrWhiteSpace(window) || !window.Contains('-')) return false;
        var parts = window.Split('-', 2);
        if (!TimeOnly.TryParse(parts[0], out var start) || !TimeOnly.TryParse(parts[1], out var end)) return false;
        var now = TimeOnly.FromDateTime(DateTime.Now);
        return start <= end ? now >= start && now <= end : now >= start || now <= end;
    }

    private AlertRule[] SnapshotRules()
    {
        lock (_sync) return _rules.ToArray();
    }

    private string GetConfigDirectory()
        => _configuration.GetValue("config:dir") ?? Environment.GetEnvironmentVariable("FortOS_CONFIG_DIR") ?? Path.Combine(Environment.GetEnvironmentVariable("FortOS_DATA_ROOT") ?? "/srv/nas", "config");

    private static bool TopicMatches(string? pattern, string value)
    {
        if (string.IsNullOrWhiteSpace(pattern) || pattern is "*" or "**") return true;
        if (pattern.Contains('*', StringComparison.Ordinal))
        {
            var expression = "^" + Regex.Escape(pattern)
                .Replace(@"\*\*", ".*", StringComparison.Ordinal)
                .Replace(@"\*", @"[^.]*", StringComparison.Ordinal) + "$";
            return Regex.IsMatch(value, expression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        return string.Equals(pattern, value, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Compare(double value, string? op, double? threshold) => op?.ToLowerInvariant() switch
    {
        "gte" or ">=" => value >= threshold,
        "gt" or ">" => value > threshold,
        "lte" or "<=" => value <= threshold,
        "lt" or "<" => value < threshold,
        "eq" or "==" => Math.Abs(value - (threshold ?? 0)) < double.Epsilon,
        _ => false
    };

    private static IReadOnlyList<AlertRule> CreateDefaultSloRules() =>
    [
        SloRule("slo-backup-freshness", "Backup freshness exceeds threshold", "warning", "fortos_backup_freshness_hours", "gt", 24),
        SloRule("slo-backup-failures", "Backup failure", "critical", "fortos_backup_failure_total", "gt", 0),
        SloRule("slo-protocol-health", "Share protocol unavailable", "critical", "fortos_protocol_health", "lt", 1),
        SloRule("slo-agent-restarts", "Agent restart storm", "warning", "fortos_agent_restarts_total", "gt", 5),
        SloRule("slo-http-errors", "HTTP 5xx errors", "warning", "fortos_http_errors_total", "gt", 0),
        SloRule("host-cpu-high", "Host CPU usage is high", "warning", "system.cpu.usage.percent", "gt", 90, 300),
        SloRule("host-memory-high", "Host memory usage is high", "warning", "system.memory.used.percent", "gt", 90, 300),
        SloRule("host-swap-high", "Host swap usage is high", "warning", "system.swap.used.percent", "gt", 50, 300),
        SloRule("host-oom-kill", "The kernel terminated a process because memory was exhausted", "critical", "system.memory.oom_kills", "gt", 0),
        SloRule("tcp-retransmits-high", "TCP retransmission rate is high", "warning", "network.tcp.retransmits_per_second", "gt", 100, 300),
        SloRule("disk-utilization-high", "Disk utilization is saturated", "warning", "storage.disk.utilization.percent", "gt", 95, 300),
        SloRule("disk-latency-high", "Disk I/O latency is high", "warning", "storage.disk.latency.milliseconds", "gt", 100, 300),
        SloRule("disk-temperature-high", "Disk temperature is high", "warning", "storage.disk.temperature.celsius", "gt", 55, 60),
        SloRule("disk-smart-failed", "Disk SMART health check failed", "critical", "storage.disk.smart.health", "lt", 1),
        SloRule("filesystem-capacity-high", "Filesystem capacity is high", "warning", "storage.filesystem.used.percent", "gt", 90, 300),
        SloRule("filesystem-capacity-critical", "Filesystem capacity is critical", "critical", "storage.filesystem.used.percent", "gt", 97, 60),
        SloRule("filesystem-exhaustion-near", "Filesystem is projected to fill within seven days", "warning", "storage.filesystem.estimated_full.seconds", "lt", 604800, 300),
        SloRule("raid-degraded", "RAID array is degraded", "critical", "storage.raid.health", "lt", 1),
        SloRule("service-unavailable", "Managed system service is unavailable", "critical", "service.health", "lt", 1, 60),
        SloRule("container-cpu-high", "Container CPU usage is high", "warning", "container.cpu.usage.percent", "gt", 90, 300),
        SloRule("container-memory-high", "Container memory usage is high", "warning", "container.memory.used.percent", "gt", 90, 300),
        EventRule("service-restart-storm", "Managed system service is repeatedly crashing", "critical", "service.*.crashed", 3, 300),
    ];

    private static AlertRule SloRule(string id, string name, string severity, string metric, string op, double value, int? durationSeconds = null) => new()
    {
        RuleId = id,
        Name = name,
        Description = $"FortOS default SLO rule: {name}",
        Severity = severity,
        Condition = new AlertCondition { Type = "metric", Metric = metric, Operator = op, Value = value, DurationSeconds = durationSeconds },
        CooldownSeconds = 300,
    };

    private static AlertRule EventRule(string id, string name, string severity, string topic, int count, int withinSeconds) => new()
    {
        RuleId = id,
        Name = name,
        Description = $"FortOS default event rule: {name}",
        Severity = severity,
        Condition = new AlertCondition { Type = "event", Topic = topic, Count = count, WithinSeconds = withinSeconds },
        CooldownSeconds = 300,
    };

    private static string BuildMetricInstanceKey(string ruleId, IReadOnlyDictionary<string, string> dimensions)
        => dimensions.Count == 0
            ? ruleId
            : ruleId + "|" + string.Join(
                "|",
                dimensions.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{pair.Key}={pair.Value}"));

    private static string FormatDimensions(IReadOnlyDictionary<string, string> dimensions)
        => dimensions.Count == 0
            ? string.Empty
            : " [" + string.Join(", ", dimensions.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}")) + "]";

    private sealed class AlertRulesDocument
    {
        /// <summary>Rule collection.</summary>
        public List<AlertRule> Rules { get; init; } = [];
    }
}
