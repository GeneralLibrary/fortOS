namespace FortOS.Tests.Integration;

public class CoreContractsTests
{
    [Fact]
    public void CoreAssemblyLoads()
    {
        Assert.Equal("FortOS.Core", typeof(FortOS.Core.ServiceDefinition).Assembly.GetName().Name);
    }
}
