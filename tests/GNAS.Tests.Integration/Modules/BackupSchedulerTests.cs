using GNAS.Core;
using GNAS.Modules.Backup.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GNAS.Tests.Integration.Modules;

public sealed class BackupSchedulerTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void IsDue_IntervalSchedule_IsDueInitially()
    {
        var scheduler = new BackupScheduler(() => Task.FromResult<IReadOnlyList<BackupTask>>([]), new RsyncBackupService(new FakeProcessManager()), new FakeEventBus(), NullLogger.Instance);
        var task = NewTask("interval:15");

        Assert.True(scheduler.IsDue(task, DateTimeOffset.UtcNow));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void IsDue_DailyTime_IsDueAfterClockTime()
    {
        var scheduler = new BackupScheduler(() => Task.FromResult<IReadOnlyList<BackupTask>>([]), new RsyncBackupService(new FakeProcessManager()), new FakeEventBus(), NullLogger.Instance);
        var task = NewTask("01:30");
        var now = new DateTimeOffset(2026, 7, 24, 2, 0, 0, TimeSpan.Zero);

        Assert.True(scheduler.IsDue(task, now));
    }

    private static BackupTask NewTask(string schedule) => new()
    {
        TaskId = "t1",
        Name = "daily",
        SourcePath = "/data",
        CronExpression = schedule,
        Method = BackupMethod.Incremental,
        Target = new BackupTarget { Type = BackupTargetType.Local, ConnectionString = "local", BucketOrPath = "/backup" }
    };

    private sealed class FakeEventBus : IEventBus
    {
        public Task PublishAsync(EventEnvelope envelope, CancellationToken ct) => Task.CompletedTask;
        public Task PublishAsync(string topic, string type, string dataJson, CancellationToken ct) => Task.CompletedTask;
        public IDisposable Subscribe(string topicPattern, Func<EventEnvelope, CancellationToken, Task> handler) => new NoopDisposable();
    }

    private sealed class FakeProcessManager : IProcessManager
    {
        public Task<ProcessInfo> StartProcessAsync(ProcessStartConfig config, CancellationToken ct) => throw new NotImplementedException();
        public Task StopProcessAsync(int pid, TimeSpan gracefulTimeout, CancellationToken ct) => throw new NotImplementedException();
        public Task<ProcessInfo?> GetProcessAsync(int pid, CancellationToken ct) => throw new NotImplementedException();
        public Task<CommandResult> ExecuteCommandAsync(ProcessStartConfig config, CancellationToken ct) => Task.FromResult(new CommandResult { ExitCode = 0 });
        public Task EnableServiceAsync(string serviceName, CancellationToken ct) => throw new NotImplementedException();
        public Task DisableServiceAsync(string serviceName, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }
}
