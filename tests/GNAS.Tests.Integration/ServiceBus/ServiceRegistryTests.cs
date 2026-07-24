using GNAS.Core;
using GNAS.ServiceBus.Registry;

namespace GNAS.Tests.Integration.ServiceBus;

public class ServiceRegistryTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task RegisterGetAndList_PersistsDefinitions()
    {
        var registry = CreateRegistry();
        var service = Definition("storage");

        await registry.RegisterAsync(service, CancellationToken.None);

        var loaded = await registry.GetAsync("storage", CancellationToken.None);
        var all = await registry.ListAsync(CancellationToken.None);
        Assert.Equal("storage", loaded!.ServiceId);
        Assert.Single(all);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateAsync_DetectsDependencyCycle()
    {
        var registry = CreateRegistry();
        await registry.RegisterAsync(Definition("c"), CancellationToken.None);
        await registry.RegisterAsync(Definition("b", "c"), CancellationToken.None);
        await registry.RegisterAsync(Definition("a", "b"), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<CircularDependencyException>(() => registry.UpdateAsync(Definition("c", "a"), CancellationToken.None));
        Assert.Contains("c", ex.Message, StringComparison.Ordinal);
        Assert.Contains("a", ex.Message, StringComparison.Ordinal);
    }

    private static ServiceRegistry CreateRegistry()
    {
        var root = Path.Combine(Environment.CurrentDirectory, "TestData", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new ServiceRegistry(new DatabaseProvider(root));
    }

    private static ServiceDefinition Definition(string id, params string[] dependsOn) => new()
    {
        ServiceId = id,
        DisplayName = id,
        Type = ServiceType.Native,
        DependsOn = dependsOn,
        Startup = ServiceStartup.Automatic,
        RestartPolicy = RestartPolicy.Never,
        Executable = "/bin/true",
    };
}
