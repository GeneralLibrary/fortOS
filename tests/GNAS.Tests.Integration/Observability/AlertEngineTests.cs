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
            Name = "事件规则",
            Description = "测试事件规则",
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
    public async Task EvaluateMetricAsync_GteThreshold_FiresAlert()
    {
        var notifier = new TestNotifier();
        var engine = CreateEngine(notifier);
        await engine.AddRuleAsync(new AlertRule
        {
            RuleId = "metric-rule",
            Name = "指标规则",
            Description = "测试指标规则",
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
            Name = "可用性规则",
            Description = "测试告警恢复",
            Severity = "critical",
            Condition = new AlertCondition { Type = "metric", Metric = "service.health", Operator = "lt", Value = 1 },
            CooldownSeconds = 0
        }, CancellationToken.None);

        await engine.EvaluateMetricAsync(new MetricData { MetricName = "service.health", Unit = "ratio", Value = 0 }, CancellationToken.None);
        Assert.Single(await engine.ListActiveAlertsAsync(CancellationToken.None));

        await engine.EvaluateMetricAsync(new MetricData { MetricName = "service.health", Unit = "ratio", Value = 1 }, CancellationToken.None);
        Assert.Empty(await engine.ListActiveAlertsAsync(CancellationToken.None));
    }

    private static AlertEngine CreateEngine(TestNotifier notifier)
    {
        var root = ObservabilityTestPaths.CreateDataRoot("alert-engine");
        var database = new DatabaseProvider(root);
        var config = new TestConfiguration().Set("config:dir", Path.Combine(root, "config"));
        Directory.CreateDirectory(Path.Combine(root, "config"));
        return new AlertEngine(config, database, new TestEventBus(), [notifier]);
    }
}
