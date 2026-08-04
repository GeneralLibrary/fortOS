using FortOS.Installer.Core.Tools;
using FortOS.Tests.Installer.Fakes;

namespace FortOS.Tests.Installer.Tools;

public class NmcliToolTests
{
    [Fact]
    public async Task ScanAsync_ParsesNetworkLines()
    {
        var runner = new FakeRunner
        {
            ExitCode = 0,
            StdoutResolver = (_, _) => "MyWiFi:80:WPA2\nGuest\\:Net:60:WPA2\nOffice5G:90:WPA3\n"
        };
        var tool = new NmcliTool(runner);

        var networks = await tool.ScanAsync(CancellationToken.None);

        Assert.Equal(3, networks.Count);
        Assert.Equal("MyWiFi", networks[0].Ssid);
        Assert.Equal("80", networks[0].Signal);
        // 转义冒号还原:Guest\:Net → Guest:Net
        Assert.Equal("Guest:Net", networks[1].Ssid);
        Assert.Equal("Office5G", networks[2].Ssid);
        Assert.Equal("WPA3", networks[2].Security);
    }

    [Fact]
    public async Task ScanAsync_NonZeroExit_ReturnsEmpty()
    {
        var runner = new FakeRunner { ExitCode = 1 };
        var tool = new NmcliTool(runner);

        var networks = await tool.ScanAsync(CancellationToken.None);

        Assert.Empty(networks);
    }

    [Fact]
    public async Task ScanAsync_EmptySsidLines_AreSkipped()
    {
        var runner = new FakeRunner { ExitCode = 0, StdoutResolver = (_, _) => "::\nWiFi5:70:WPA2\n" };
        var tool = new NmcliTool(runner);

        var networks = await tool.ScanAsync(CancellationToken.None);

        var network = Assert.Single(networks);
        Assert.Equal("WiFi5", network.Ssid);
    }

    [Fact]
    public async Task ConnectAsync_Success_PassesPasswordAsArgument()
    {
        var runner = new FakeRunner { ExitCode = 0 };
        var tool = new NmcliTool(runner);

        var (ok, error) = await tool.ConnectAsync("MyWiFi", "secret", CancellationToken.None);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Contains("MyWiFi", runner.Calls[0].Args);
        Assert.Contains("secret", runner.Calls[0].Args);
    }

    [Fact]
    public async Task ConnectAsync_Failure_ReturnsErrorDetail()
    {
        var runner = new FakeRunner
        {
            ExitCode = 1,
            StdoutResolver = (_, _) => "Error: connection activation failed"
        };
        var tool = new NmcliTool(runner);

        var (ok, error) = await tool.ConnectAsync("MyWiFi", null, CancellationToken.None);

        Assert.False(ok);
        Assert.Contains("connection activation failed", error);
    }

    [Fact]
    public async Task ConnectAsync_OpenNetwork_OmitsPasswordArgument()
    {
        var runner = new FakeRunner { ExitCode = 0 };
        var tool = new NmcliTool(runner);

        var (ok, _) = await tool.ConnectAsync("OpenNet", string.Empty, CancellationToken.None);

        Assert.True(ok);
        Assert.DoesNotContain(runner.Calls[0].Args, arg => arg == "password");
    }
}
