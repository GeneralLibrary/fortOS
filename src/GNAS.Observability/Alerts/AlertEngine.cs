using System.Text.Json;
using GNAS.Core;
using GNAS.Observability.Alerts.Notifiers;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace GNAS.Observability.Alerts;

/// <summary>告警引擎和事件订阅后台服务。</summary>
public sealed class AlertEngine : IAlertEngine, IHostedService, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IGnasConfiguration _configuration;
    private readonly IDatabaseProvider _database;
    private readonly IEventBus _eventBus;
    private readonly IReadOnlyList<INotifier> _notifiers;
    private readonly object _sync = new();
    private readonly List<AlertRule> _rules = [];
    private readonly List<ActiveAlert> _activeAlerts = [];
    private readonly Dictionary<string, DateTimeOffset> _lastFired = [];
    private readonly Dictionary<string, List<DateTimeOffset>> _eventWindows = [];
    private readonly Dictionary<string, DateTimeOffset> _metricSince = [];
    private IDisposable? _subscription;

    /// <summary>初始化告警引擎。</summary>
    public AlertEngine(IGnasConfiguration configuration, IDatabaseProvider database, IEventBus eventBus, IEnumerable<INotifier> notifiers)
    {
        _configuration = configuration;
        _database = database;
        _eventBus = eventBus;
        _notifiers = notifiers.ToArray();
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
            if (rule.Condition.WithinSeconds is { } windowSeconds && rule.Condition.Count is { } count)
            {
                var key = rule.RuleId;
                List<DateTimeOffset> bucket;
                lock (_sync)
                {
                    bucket = _eventWindows.TryGetValue(key, out var existing) ? existing : (_eventWindows[key] = []);
                    bucket.Add(now);
                    bucket.RemoveAll(item => item < now.AddSeconds(-windowSeconds));
                    if (bucket.Count < count) continue;
                }
            }

            await FireAsync(rule, $"事件规则 {rule.Name} 已触发：{envelope.Topic}", ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task EvaluateMetricAsync(MetricData metric, CancellationToken ct)
    {
        foreach (var rule in SnapshotRules().Where(rule => rule.Condition.Type.Equals("metric", StringComparison.OrdinalIgnoreCase)))
        {
            if (!string.Equals(rule.Condition.Metric, metric.MetricName, StringComparison.OrdinalIgnoreCase)) continue;
            if (!Compare(metric.Value, rule.Condition.Operator, rule.Condition.Value))
            {
                lock (_sync) _metricSince.Remove(rule.RuleId);
                await ResolveAsync(rule, metric, ct).ConfigureAwait(false);
                continue;
            }

            if (rule.Condition.DurationSeconds is { } durationSeconds and > 0)
            {
                var now = DateTimeOffset.UtcNow;
                lock (_sync)
                {
                    if (!_metricSince.TryGetValue(rule.RuleId, out var since))
                    {
                        _metricSince[rule.RuleId] = now;
                        continue;
                    }

                    if (now - since < TimeSpan.FromSeconds(durationSeconds)) continue;
                }
            }

            await FireAsync(rule, $"指标规则 {rule.Name} 已触发：{metric.MetricName}={metric.Value}", ct).ConfigureAwait(false);
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
    public void Dispose() => _subscription?.Dispose();

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

    private async Task FireAsync(AlertRule rule, string message, CancellationToken ct)
    {
        if (IsSuppressed(rule)) return;
        var now = DateTimeOffset.UtcNow;
        lock (_sync)
        {
            if (_lastFired.TryGetValue(rule.RuleId, out var last) && now - last < TimeSpan.FromSeconds(rule.CooldownSeconds)) return;
            _lastFired[rule.RuleId] = now;
        }

        var alert = new ActiveAlert
        {
            AlertId = Guid.CreateVersion7().ToString(),
            RuleId = rule.RuleId,
            Severity = rule.Severity,
            Message = message,
            TriggeredAt = now
        };
        lock (_sync) _activeAlerts.Add(alert);

        foreach (var notifier in SelectNotifiers(rule.Severity))
        {
            await notifier.NotifyAsync(alert, rule, ct).ConfigureAwait(false);
        }
    }

    private async Task ResolveAsync(AlertRule rule, MetricData metric, CancellationToken ct)
    {
        ActiveAlert[] resolved;
        lock (_sync)
        {
            resolved = _activeAlerts.Where(alert => alert.RuleId == rule.RuleId).ToArray();
            if (resolved.Length == 0) return;
            _activeAlerts.RemoveAll(alert => alert.RuleId == rule.RuleId);
            _lastFired.Remove(rule.RuleId);
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

    private IEnumerable<INotifier> SelectNotifiers(string severity)
    {
        var lower = severity.ToLowerInvariant();
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
        => _configuration.GetValue("config:dir") ?? Environment.GetEnvironmentVariable("GNAS_CONFIG_DIR") ?? Path.Combine(Environment.GetEnvironmentVariable("GNAS_DATA_ROOT") ?? "/srv/nas", "config");

    private static bool TopicMatches(string? pattern, string value)
    {
        if (string.IsNullOrWhiteSpace(pattern) || pattern is "*" or "**") return true;
        if (pattern.EndsWith("**", StringComparison.Ordinal)) return value.StartsWith(pattern[..^2], StringComparison.OrdinalIgnoreCase);
        if (pattern.EndsWith('*')) return value.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase);
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
        SloRule("slo-backup-freshness", "备份新鲜度超标", "warning", "gnas_backup_freshness_hours", "gt", 24),
        SloRule("slo-backup-failures", "备份失败", "critical", "gnas_backup_failure_total", "gt", 0),
        SloRule("slo-protocol-health", "共享协议不可用", "critical", "gnas_protocol_health", "lt", 1),
        SloRule("slo-agent-restarts", "Agent 重启风暴", "warning", "gnas_agent_restarts_total", "gt", 5),
        SloRule("slo-http-errors", "HTTP 5xx 错误", "warning", "gnas_http_errors_total", "gt", 0),
    ];

    private static AlertRule SloRule(string id, string name, string severity, string metric, string op, double value) => new()
    {
        RuleId = id,
        Name = name,
        Description = $"GNAS 默认 SLO 规则：{name}",
        Severity = severity,
        Condition = new AlertCondition { Type = "metric", Metric = metric, Operator = op, Value = value },
        CooldownSeconds = 300,
    };

    private sealed class AlertRulesDocument
    {
        /// <summary>规则集合。</summary>
        public List<AlertRule> Rules { get; init; } = [];
    }
}
