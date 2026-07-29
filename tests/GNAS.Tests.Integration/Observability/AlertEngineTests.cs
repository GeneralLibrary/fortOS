using GNAS.Core;
using global::GNAS.Observability.Alerts;

namespace GNAS.Tests.Integration.Observability;

public sealed class AlertEngineTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task EvaluateEventAsync_FiresOnceThenCooldownSuppresses()
    {
        var notifier = new TestNotifier();
        var engine = CreateEngine(notifier);
        await engine.AddRuleAsync(new AlertRule
        {
            RuleId = "event-rule",
            Name = "Event Rule",
            Description = "Test event rule",
            Severity = "warning",
            Condition = new AlertCondition { Type = "event", Topic = "disk.failed" },
            CooldownSeconds = 60
        }, CancellationToken.None);

        var envelope = new EventEnvelope { Topic = "disk.failed", Type = "disk.failed", DataJson = "{}" };
        await engine.EvaluateEventAsync(envelope, CancellationToken.None);
        await engine.EvaluateEventAsync(envelope, CancellationToken.None);

        var alerts = await engine.ListActiveAlertsAsync(CancellationToken.None);
        Assert.Single(alerts);
        Assert.Single(notifier.Alerts);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task EvaluateEventAsync_MatchesWildcardWithinTopic()
    {
        var notifier = new TestNotifier();
        var engine = CreateEngine(notifier);
        await engine.AddRuleAsync(new AlertRule
        {
            RuleId = "service-crash",
            Name = "Service crash",
            Description = "Matches a service identifier",
            Severity = "critical",
            Condition = new AlertCondition { Type = "event", Topic = "service.*.crashed" },
        }, CancellationToken.None);

        await engine.EvaluateEventAsync(
            new EventEnvelope { Topic = "service.smb-daemon.crashed", Type = "service.crashed", DataJson = "{}" },
            CancellationToken.None);

        Assert.Single(await engine.ListActiveAlertsAsync(CancellationToken.None));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task EvaluateEventAsync_CountsResourcesIndependentlyAndResolvesAfterQuietPeriod()
    {
        var notifier = new TestNotifier();
        using var engine = CreateEngine(notifier);
        await engine.AddRuleAsync(new AlertRule
        {
            RuleId = "service-crash",
            Name = "Service crash",
            Description = "Per-service crash window",
            Severity = "critical",
            Condition = new AlertCondition
            {
                Type = "event",
                Topic = "service.*.crashed",
                Count = 2,
                WithinSeconds = 1,
            },
        }, CancellationToken.None);

        await engine.EvaluateEventAsync(Event("service.smb.crashed"), CancellationToken.None);
        await engine.EvaluateEventAsync(Event("service.ssh.crashed"), CancellationToken.None);
        Assert.Empty(await engine.ListActiveAlertsAsync(CancellationToken.None));

        await engine.EvaluateEventAsync(Event("service.smb.crashed"), CancellationToken.None);
        var active = Assert.Single(await engine.ListActiveAlertsAsync(CancellationToken.None));
        Assert.Equal("service.smb.crashed", active.Dimensions["topic"]);

        await Task.Delay(TimeSpan.FromMilliseconds(1_200));
        Assert.Empty(await engine.ListActiveAlertsAsync(CancellationToken.None));
        Assert.Single(notifier.ResolvedAlerts);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task EvaluateMetricAsync_GteThreshold_FiresAlert()
    {
        var notifier = new TestNotifier();
        var engine = CreateEngine(notifier);
        await engine.AddRuleAsync(new AlertRule
        {
            RuleId = "metric-rule",
            Name = "Metric Rule",
            Description = "Test metric rule",
            Severity = "critical",
            Condition = new AlertCondition { Type = "metric", Metric = "cpu.usage", Operator = "gte", Value = 90 },
            CooldownSeconds = 0
        }, CancellationToken.None);

        await engine.EvaluateMetricAsync(new MetricData { MetricName = "cpu.usage", Unit = "percent", Value = 95 }, CancellationToken.None);

        var alerts = await engine.ListActiveAlertsAsync(CancellationToken.None);
        Assert.Single(alerts);
        Assert.Equal("critical", alerts.Single().Severity);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task EvaluateMetricAsync_HealthyValueResolvesActiveAlert()
    {
        var notifier = new TestNotifier();
        var engine = CreateEngine(notifier);
        await engine.AddRuleAsync(new AlertRule
        {
            RuleId = "availability-rule",
            Name = "Availability Rule",
            Description = "Test alert recovery",
            Severity = "critical",
            Condition = new AlertCondition { Type = "metric", Metric = "service.health", Operator = "lt", Value = 1 },
            CooldownSeconds = 0
        }, CancellationToken.None);

        await engine.EvaluateMetricAsync(new MetricData { MetricName = "service.health", Unit = "ratio", Value = 0 }, CancellationToken.None);
        Assert.Single(await engine.ListActiveAlertsAsync(CancellationToken.None));

        await engine.EvaluateMetricAsync(new MetricData { MetricName = "service.health", Unit = "ratio", Value = 1 }, CancellationToken.None);
        Assert.Empty(await engine.ListActiveAlertsAsync(CancellationToken.None));
        Assert.Single(notifier.ResolvedAlerts);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task EvaluateMetricAsync_DimensionsKeepResourceAlertsIndependent()
    {
        var notifier = new TestNotifier();
        var engine = CreateEngine(notifier);
        await engine.AddRuleAsync(new AlertRule
        {
            RuleId = "disk-temperature",
            Name = "Disk temperature",
            Description = "Per-disk threshold",
            Severity = "warning",
            Condition = new AlertCondition { Type = "metric", Metric = "storage.disk.temperature.celsius", Operator = "gt", Value = 55 },
        }, CancellationToken.None);

        await engine.EvaluateMetricAsync(Metric("sda", 60), CancellationToken.None);
        await engine.EvaluateMetricAsync(Metric("sdb", 65), CancellationToken.None);
        Assert.Equal(2, (await engine.ListActiveAlertsAsync(CancellationToken.None)).Count);

        await engine.EvaluateMetricAsync(Metric("sda", 40), CancellationToken.None);
        var remaining = Assert.Single(await engine.ListActiveAlertsAsync(CancellationToken.None));
        Assert.Equal("sdb", remaining.Dimensions["disk"]);
        Assert.Single(notifier.ResolvedAlerts);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task EvaluateMetricAsync_NotifierFailureDoesNotBlockOtherChannelsAndRetries()
    {
        var failing = new FailingNotifier();
        var successful = new TestNotifier();
        var engine = CreateEngine([failing, successful]);
        await engine.AddRuleAsync(new AlertRule
        {
            RuleId = "notification-retry",
            Name = "Notification retry",
            Description = "Retries only failed channels",
            Severity = "critical",
            Condition = new AlertCondition { Type = "metric", Metric = "cpu.usage", Operator = "gt", Value = 90 },
        }, CancellationToken.None);

        var metric = new MetricData { MetricName = "cpu.usage", Unit = "percent", Value = 95 };
        await engine.EvaluateMetricAsync(metric, CancellationToken.None);
        await engine.EvaluateMetricAsync(metric, CancellationToken.None);

        Assert.Equal(2, failing.Attempts);
        Assert.Single(successful.Alerts);
    }

    private static AlertEngine CreateEngine(TestNotifier notifier)
        => CreateEngine([notifier]);

    private static AlertEngine CreateEngine(IEnumerable<global::GNAS.Observability.Alerts.Notifiers.INotifier> notifiers)
    {
        var root = ObservabilityTestPaths.CreateDataRoot("alert-engine");
        var database = new DatabaseProvider(root);
        var config = new TestConfiguration().Set("config:dir", Path.Combine(root, "config"));
        Directory.CreateDirectory(Path.Combine(root, "config"));
        return new AlertEngine(config, database, new TestEventBus(), notifiers);
    }

    private static MetricData Metric(string disk, double temperature) => new()
    {
        MetricName = "storage.disk.temperature.celsius",
        Unit = "celsius",
        Value = temperature,
        Dimensions = new Dictionary<string, string> { ["disk"] = disk },
    };

    private static EventEnvelope Event(string topic) => new()
    {
        Topic = topic,
        Type = "service.crashed",
        DataJson = "{}",
    };

    private sealed class FailingNotifier : global::GNAS.Observability.Alerts.Notifiers.INotifier
    {
        public int Attempts { get; private set; }

        public Task NotifyAsync(ActiveAlert alert, AlertRule rule, CancellationToken ct)
        {
            Attempts++;
            throw new InvalidOperationException("Notification channel unavailable.");
        }
    }
}
