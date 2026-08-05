using FortOS.Core;
using FortOS.Modules.Backup.Services;

namespace FortOS.Tests.Integration.Modules;

/// <summary>
/// RsyncBackupService 数据保护回归测试：空源配合 --delete 会清空目标目录，
/// 必须拒绝执行（挂载失败/路径错误的常见形态），并验证 exclude 参数拼接。
/// </summary>
public sealed class RsyncBackupServiceTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task SyncAsync_MissingSource_RefusesWithCode3()
    {
        var service = new RsyncBackupService(new RecordingProcessManager());
        var missing = Path.Combine("TestArtifacts", nameof(RsyncBackupServiceTests), "does-not-exist-" + Guid.NewGuid().ToString("N"));

        var result = await service.SyncAsync(missing, "/tmp/target", dryRun: false, CancellationToken.None);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("refusing", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SyncAsync_EmptySource_RefusesWithCode3()
    {
        var service = new RsyncBackupService(new RecordingProcessManager());
        var empty = Path.Combine("TestArtifacts", nameof(RsyncBackupServiceTests), "empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);

        var result = await service.SyncAsync(empty, "/tmp/target", dryRun: false, CancellationToken.None);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("empty", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SyncAsync_NonEmptySource_PassesExcludePatternsToRsync()
    {
        var process = new RecordingProcessManager();
        var service = new RsyncBackupService(process);
        var source = Path.Combine("TestArtifacts", nameof(RsyncBackupServiceTests), "source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "keep.txt"), "data");

        await service.SyncAsync(source, "/tmp/target", dryRun: true, CancellationToken.None, ["node_modules", "*.tmp"]);

        Assert.NotNull(process.LastArguments);
        Assert.Contains("--dry-run", process.LastArguments);
        Assert.Contains("--exclude=\"node_modules\"", process.LastArguments);
        Assert.Contains("--exclude=\"*.tmp\"", process.LastArguments);
        Assert.Contains("--delete", process.LastArguments);
    }

    /// <summary>记录最近一次命令调用，避免真实启动 rsync。</summary>
    private sealed class RecordingProcessManager : IProcessManager
    {
        public string? LastArguments { get; private set; }

        public Task<ProcessInfo> StartProcessAsync(ProcessStartConfig config, CancellationToken ct) => throw new NotSupportedException();
        public Task StopProcessAsync(int pid, TimeSpan gracefulTimeout, CancellationToken ct) => throw new NotSupportedException();
        public Task<ProcessInfo?> GetProcessAsync(int pid, CancellationToken ct) => throw new NotSupportedException();

        public Task<CommandResult> ExecuteCommandAsync(ProcessStartConfig config, CancellationToken ct)
        {
            LastArguments = config.Arguments;
            return Task.FromResult(new CommandResult { ExitCode = 0 });
        }

        public Task EnableServiceAsync(string serviceName, CancellationToken ct) => throw new NotSupportedException();
        public Task DisableServiceAsync(string serviceName, CancellationToken ct) => throw new NotSupportedException();
    }
}
