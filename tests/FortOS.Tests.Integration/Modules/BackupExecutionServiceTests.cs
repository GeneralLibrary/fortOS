using System.Security.Cryptography;
using System.Text.Json;
using FortOS.Core;
using FortOS.Modules.Backup.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace FortOS.Tests.Integration.Modules;

/// <summary>
/// F2 数据保护回归测试：restore 成功路径必须先写 Succeeded 记录再清理 checkpoint；
/// 完成事件发布失败绝不能把已成功的 restore 拖入回滚（否则恢复数据与备份副本双丢）。
/// </summary>
public sealed class BackupExecutionServiceTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task Restore_Success_EventPublishFailure_StillSucceeds_AndCheckpointIsCleaned()
    {
        var root = Path.Combine("TestArtifacts", nameof(BackupExecutionServiceTests), Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        var target = Path.Combine(root, "target");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(Path.Combine(source, "file1.txt"), "hello world");

        // 构造合法 manifest（restore 模式 VerifyManifestAsync 会校验 source 文件哈希）。
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(File.OpenRead(Path.Combine(source, "file1.txt"))));
        await File.WriteAllTextAsync(Path.Combine(source, ".fortos-checksums.json"), JsonSerializer.Serialize(new Dictionary<string, string> { ["file1.txt"] = hash }));

        // 目标已有旧数据：触发 checkpoint 创建，用于验证成功后的清理时机。
        await File.WriteAllTextAsync(Path.Combine(target, "old.txt"), "old data");

        var db = new DatabaseProvider(root);
        var executor = new BackupExecutionService(
            new FakeProcessManager(),
            new FailingEventBus(),
            new BackupRunHistoryStore(db),
            new SqliteLeaseService(db),
            NullLogger<BackupExecutionService>.Instance);

        var task = new BackupTask
        {
            TaskId = "t1",
            Name = "test restore",
            SourcePath = source,
            CronExpression = "interval:60",
            Target = new BackupTarget { Type = BackupTargetType.Local, ConnectionString = string.Empty, BucketOrPath = target },
        };

        var record = await executor.RestoreAsync(task, source, target, dryRun: false, CancellationToken.None);

        Assert.True(record.Success);
        Assert.Equal(BackupRunState.Succeeded, record.State);
        // checkpoint 已被清理（best-effort），且失败的事件发布没有触发回滚。
        Assert.Empty(Directory.EnumerateDirectories(root, "*.fortos-checkpoint-*"));
    }

    /// <summary>rsync 模拟：任何命令都成功返回。</summary>
    private sealed class FakeProcessManager : IProcessManager
    {
        public Task<ProcessInfo> StartProcessAsync(ProcessStartConfig config, CancellationToken ct) => throw new NotSupportedException();
        public Task StopProcessAsync(int pid, TimeSpan gracefulTimeout, CancellationToken ct) => throw new NotSupportedException();
        public Task<ProcessInfo?> GetProcessAsync(int pid, CancellationToken ct) => throw new NotSupportedException();
        public Task<CommandResult> ExecuteCommandAsync(ProcessStartConfig config, CancellationToken ct) => Task.FromResult(new CommandResult { ExitCode = 0 });
        public Task EnableServiceAsync(string serviceName, CancellationToken ct) => throw new NotSupportedException();
        public Task DisableServiceAsync(string serviceName, CancellationToken ct) => throw new NotSupportedException();
    }

    /// <summary>完成事件发布失败：验证其不会把已成功的 restore 标记为失败/回滚。</summary>
    private sealed class FailingEventBus : IEventBus
    {
        public Task PublishAsync(EventEnvelope envelope, CancellationToken ct) => throw new InvalidOperationException("Event bus is down.");
        public Task PublishAsync(string topic, string type, string dataJson, CancellationToken ct) => throw new InvalidOperationException("Event bus is down.");
        public IDisposable Subscribe(string topicPattern, Func<EventEnvelope, CancellationToken, Task> handler) => throw new NotSupportedException();
    }
}
