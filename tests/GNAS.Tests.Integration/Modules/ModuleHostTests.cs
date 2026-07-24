using GNAS.Core;
using GNAS.Modules.Host;
using Microsoft.Extensions.Logging.Abstractions;

namespace GNAS.Tests.Integration.Modules;

public sealed class ModuleHostTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task DiscoverAndLoadAsync_InitializesBuiltInsInDependencyOrder()
    {
        var order = new List<string>();
        await using var scope = new DataRootScope();
        using var host = new ModuleHost(new EmptyServiceProvider(), new RecordingEventBus(), NullLoggerFactory.Instance,
            [new RecordingModule("share", ["storage"], order), new RecordingModule("storage", [], order)]);

        var loaded = await host.DiscoverAndLoadAsync(CancellationToken.None);

        Assert.Equal(["storage", "share"], order);
        Assert.Equal(["storage", "share"], loaded.Select(m => m.ModuleId).ToArray());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DiscoverAndLoadAsync_SkipsBuiltInWithMissingDependency()
    {
        await using var scope = new DataRootScope();
        using var host = new ModuleHost(new EmptyServiceProvider(), new RecordingEventBus(), NullLoggerFactory.Instance,
            [new RecordingModule("share", ["storage"], [])]);

        var loaded = await host.DiscoverAndLoadAsync(CancellationToken.None);

        Assert.Empty(loaded);
    }

    private sealed class RecordingModule(string id, IReadOnlyList<string> dependencies, List<string> order) : INasModule
    {
        public string ModuleId => id;
        public string DisplayName => id;
        public Version Version => new(1, 0, 0);
        public IReadOnlyList<string> RequiredCapabilities => [];
        public IReadOnlyList<string> Dependencies => dependencies;
        public Task InitializeAsync(ModuleContext context, CancellationToken ct) { order.Add(id); return Task.CompletedTask; }
        public Task ShutdownAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<HealthStatus> CheckHealthAsync(CancellationToken ct) => Task.FromResult(HealthStatus.Healthy);
    }

    private sealed class RecordingEventBus : IEventBus
    {
        public Task PublishAsync(EventEnvelope envelope, CancellationToken ct) => Task.CompletedTask;
        public Task PublishAsync(string topic, string type, string dataJson, CancellationToken ct) => Task.CompletedTask;
        public IDisposable Subscribe(string topicPattern, Func<EventEnvelope, CancellationToken, Task> handler) => new NoopDisposable();
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }

    private sealed class DataRootScope : IAsyncDisposable
    {
        private readonly string? old = Environment.GetEnvironmentVariable("GNAS_DATA_ROOT");
        public string Root { get; } = Path.Combine(Directory.GetCurrentDirectory(), "TestData", Guid.NewGuid().ToString("N"));
        public DataRootScope() => Environment.SetEnvironmentVariable("GNAS_DATA_ROOT", Root);
        public ValueTask DisposeAsync()
        {
            Environment.SetEnvironmentVariable("GNAS_DATA_ROOT", old);
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
            return ValueTask.CompletedTask;
        }
    }
}
