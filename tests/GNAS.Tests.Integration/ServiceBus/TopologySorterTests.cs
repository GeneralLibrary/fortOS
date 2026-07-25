using GNAS.Core;
using GNAS.ServiceBus.Supervisor;

namespace GNAS.Tests.Integration.ServiceBus;

public class TopologySorterTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void SortLevels_StartsDependenciesBeforeDependents()
    {
        var services = new[]
        {
            Definition("api", "registry", "events"),
            Definition("registry", "database"),
            Definition("events"),
            Definition("database"),
        };

        var levels = TopologySorter.SortLevels(services);

        Assert.Contains(levels[0], s => s.ServiceId == "database");
        Assert.Contains(levels[0], s => s.ServiceId == "events");
        Assert.Contains(levels[1], s => s.ServiceId == "registry");
        Assert.Contains(levels[2], s => s.ServiceId == "api");
    }

    private static ServiceDefinition Definition(string id, params string[] dependsOn) => new()
    {
        ServiceId = id,
        DisplayName = id,
        Type = ServiceType.Native,
        DependsOn = dependsOn,
        Startup = ServiceStartup.Automatic,
        RestartPolicy = RestartPolicy.ExponentialBackoff,
        Executable = "/bin/true",
    };
}
