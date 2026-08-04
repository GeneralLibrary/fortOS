using System.Net;
using FortOS.Installer.Gui.Networking;

namespace FortOS.Tests.Installer.Gui;

public class NetworkInfoTests
{
    [Theory]
    [InlineData("lo", true)]
    [InlineData("docker0", true)]
    [InlineData("veth1234", true)]
    [InlineData("br-abc", true)]
    [InlineData("virbr0", true)]
    [InlineData("vnic-x", true)]
    [InlineData("eth0", false)]
    [InlineData("wlan0", false)]
    [InlineData("enp3s0", false)]
    [InlineData("ens33", false)]
    public void IsVirtualInterface_ClassifiesNames(string name, bool expected)
        => Assert.Equal(expected, NetworkInfo.IsVirtualInterface(name));

    [Fact]
    public void IsLinkLocal_Detects169_254()
        => Assert.True(NetworkInfo.IsLinkLocal(IPAddress.Parse("169.254.1.1")));

    [Fact]
    public void IsLinkLocal_RejectsPrivateAndPublic()
    {
        Assert.False(NetworkInfo.IsLinkLocal(IPAddress.Parse("192.168.1.5")));
        Assert.False(NetworkInfo.IsLinkLocal(IPAddress.Parse("8.8.8.8")));
    }
}
