using FortOS.Installer.Core.Models;
using FortOS.Installer.Core.Tools;
using FortOS.Tests.Installer.Fakes;

namespace FortOS.Tests.Installer.Tools;

public class SgdiskToolTests
{
    [Fact]
    public async Task CreatePartitions_BuildsNewTypeCodeNameArgs()
    {
        var runner = new FakeRunner();
        var tool = new SgdiskTool(runner);

        await tool.CreatePartitionsAsync(
            "/dev/sda",
            [
                new PartitionSpec { Number = 1, SizeMiB = 1, TypeCode = "ef02", Label = "BIOS boot" },
                new PartitionSpec { Number = 2, SizeMiB = 512, TypeCode = "ef00", Label = "EFI System" },
                new PartitionSpec { Number = 3, SizeMiB = 0, TypeCode = "8304", Label = "FortOS root" },
            ],
            CancellationToken.None);

        var call = runner.Calls.Single(c => c.File == "sgdisk");
        Assert.Contains("--new=1:0:+1M", call.Args);
        Assert.Contains("--typecode=1:ef02", call.Args);
        Assert.Contains("--change-name=1:BIOS boot", call.Args);
        Assert.Contains("--new=3:0:0", call.Args); // 0 = 剩余空间
        Assert.Equal("/dev/sda", call.Args.Last());
    }

    [Fact]
    public async Task Zap_UsesZapAll()
    {
        var runner = new FakeRunner();
        var tool = new SgdiskTool(runner);

        await tool.ZapAsync("/dev/sdb", CancellationToken.None);

        var call = runner.Calls.Single(c => c.File == "sgdisk");
        Assert.Equal(["--zap-all", "/dev/sdb"], call.Args);
    }
}
