namespace GORT.Tests.Integration;

public class CoreContractsTests
{
    [Fact]
    public void CoreAssemblyLoads()
    {
        Assert.Equal("GORT.Core", typeof(GORT.Core.ServiceDefinition).Assembly.GetName().Name);
    }
}
