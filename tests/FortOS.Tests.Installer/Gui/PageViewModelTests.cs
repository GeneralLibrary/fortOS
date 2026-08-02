using FortOS.Installer.Core.Models;
using FortOS.Installer.Core.Tools;
using FortOS.Installer.Gui.ViewModels;
using FortOS.Tests.Installer.Fakes;

namespace FortOS.Tests.Installer.Gui;

[Collection("Gui.Localization")]
public class PageViewModelTests
{
    private const string DisksJson = """
        {"blockdevices":[
          {"name":"sda","path":"/dev/sda","size":21474836480,"type":"disk","rota":0,"rm":0,"ro":0},
          {"name":"sdb","path":"/dev/sdb","size":107374182400,"type":"disk","rota":0,"rm":0,"ro":0},
          {"name":"sr0","path":"/dev/sr0","size":1000000000,"type":"disk","rota":0,"rm":1,"ro":1}
        ]}
        """;

    [Fact]
    public async Task DiskLayout_LoadAsync_FiltersReadOnlyMedia()
    {
        var runner = new FakeRunner { StdoutResolver = (_, _) => DisksJson };
        var vm = new DiskLayoutViewModel(new LsblkTool(runner));

        await vm.LoadAsync();

        Assert.Equal(2, vm.Disks.Count); // sr0(只读光驱)被过滤
        Assert.All(vm.Disks, d => Assert.False(d.IsReadOnly));
        Assert.Equal(string.Empty, vm.Error);
    }

    [Fact]
    public async Task DiskLayout_IsValid_RequiresSystemDiskAndDistinctDataDisk()
    {
        var runner = new FakeRunner { StdoutResolver = (_, _) => DisksJson };
        var vm = new DiskLayoutViewModel(new LsblkTool(runner));
        await vm.LoadAsync();

        Assert.False(vm.IsValid); // 未选系统盘

        vm.SelectedSystemDisk = vm.Disks[0];
        Assert.True(vm.IsValid); // 数据盘 None 模式

        vm.DataMode = DataDiskMode.Single;
        Assert.False(vm.IsValid); // 未选数据盘

        vm.SelectedDataDisk = vm.Disks[0];
        Assert.False(vm.IsValid); // 系统盘 == 数据盘

        vm.SelectedDataDisk = vm.Disks[1];
        Assert.True(vm.IsValid);
        Assert.True(vm.ShowDataDiskPanel);
    }

    [Fact]
    public void Network_IsValid_DhcpNeedsNoAddress()
    {
        var vm = new NetworkViewModel { Hostname = "nas" };
        Assert.True(vm.IsValid);

        vm.Mode = NetworkMode.Static;
        Assert.False(vm.IsValid);
        vm.Address = "192.168.1.5/24";
        Assert.True(vm.IsValid);
    }

    [Fact]
    public void Account_IsValid_PasswordMatchAndStrength()
    {
        var vm = new AccountViewModel
        {
            Username = "admin",
            Password = "short",
            ConfirmPassword = "short",
        };
        Assert.False(vm.IsValid); // 密码过短
        Assert.Equal("Password strength: Weak", vm.PasswordStrength);

        vm.Password = "correct-horse-battery";
        vm.ConfirmPassword = "correct-horse-battery";
        Assert.True(vm.IsValid);
        Assert.Equal("Password strength: Strong", vm.PasswordStrength);

        vm.ConfirmPassword = "mismatch";
        Assert.False(vm.IsValid);
    }

    [Fact]
    public void Confirm_SummaryReflectsChoices()
    {
        var welcome = new WelcomeViewModel();
        var disk = new DiskLayoutViewModel(new LsblkTool(new FakeRunner()))
        {
            SelectedSystemDisk = new DiskInfo { Name = "sda", Path = "/dev/sda", SizeBytes = 20_000_000_000 },
            DataMode = DataDiskMode.None,
        };
        var network = new NetworkViewModel { Hostname = "nas-1" };
        var account = new AccountViewModel { Username = "admin", Password = "x123456", ConfirmPassword = "x123456", Timezone = "UTC" };
        var confirm = new ConfirmViewModel(welcome, disk, network, account);

        var summary = confirm.Summary;

        Assert.Contains("/dev/sda", summary);
        Assert.Contains("nas-1", summary);
        Assert.Contains("admin", summary);
        Assert.Contains("not configured (post-install)", summary);
        Assert.Contains("DHCP", summary);
    }
}
