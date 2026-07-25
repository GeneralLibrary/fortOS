namespace GNAS.Tests.Integration;

public class CoreContractsTests
{
    [Fact]
    public void CoreAssemblyLoads()
    {
        Assert.Equal("GNAS.Core", typeof(GNAS.Core.ServiceDefinition).Assembly.GetName().Name);
    }
}
