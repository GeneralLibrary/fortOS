using GORT.Modules.Backup.Services;

namespace GORT.Tests.Integration.Modules;

public class BackupRunHistoryStoreTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task AppendAndQuery_ReturnsLatestFirst()
    {
        using var root = new TemporaryDataRoot(nameof(AppendAndQuery_ReturnsLatestFirst));
        var store = new BackupRunHistoryStore();

        await store.AppendAsync(new BackupRunRecord
        {
            RunId = "run-1",
            TaskId = "task-a",
            Operation = "run",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            FinishedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            Success = true,
            ExitCode = 0,
            Stdout = "ok",
            Stderr = string.Empty,
        }, CancellationToken.None);
        await store.AppendAsync(new BackupRunRecord
        {
            RunId = "run-2",
            TaskId = "task-a",
            Operation = "restore",
            StartedAt = DateTimeOffset.UtcNow,
            FinishedAt = DateTimeOffset.UtcNow,
            Success = false,
            ExitCode = 1,
            Stdout = string.Empty,
            Stderr = "failed",
        }, CancellationToken.None);

        var records = await store.QueryAsync("task-a", 10, CancellationToken.None);

        Assert.Equal(2, records.Count);
        Assert.Equal("run-2", records[0].RunId);
        Assert.Equal("run-1", records[1].RunId);
    }

    private sealed class TemporaryDataRoot : IDisposable
    {
        private readonly string? _previous;

        public TemporaryDataRoot(string name)
        {
            _previous = Environment.GetEnvironmentVariable("GORT_DATA_ROOT");
            Root = Path.GetFullPath(Path.Combine("TestArtifacts", "Modules", name, Guid.CreateVersion7().ToString()));
            Directory.CreateDirectory(Root);
            Environment.SetEnvironmentVariable("GORT_DATA_ROOT", Root);
        }

        public string Root { get; }

        public void Dispose() => Environment.SetEnvironmentVariable("GORT_DATA_ROOT", _previous);
    }
}
