using FortOS.Installer.Core.Models;
using FortOS.Installer.Core.Tools;
using FortOS.Tests.Installer.Fakes;

namespace FortOS.Tests.Installer.Tools;

public class MkfsToolTests
{
    [Theory]
    [InlineData(PartitionFs.Ext4, "mkfs.ext4", "-F", "-L")]
    [InlineData(PartitionFs.Btrfs, "mkfs.btrfs", "-f", "-L")]
    [InlineData(PartitionFs.Vfat, "mkfs.fat", "-F", "-n")] // dosfstools 卷标用 -n
    [InlineData(PartitionFs.Xfs, "mkfs.xfs", "-f", "-L")]
    [InlineData(PartitionFs.Swap, "mkswap", "", "-L")]
    public async Task Format_UsesCorrectBinary(PartitionFs fs, string expectedBinary, string expectedFlag, string expectedLabelFlag)
    {
        var runner = new FakeRunner();
        var tool = new MkfsTool(runner);

        await tool.FormatAsync("/dev/sda3", fs, "FORTOS", CancellationToken.None);

        var call = runner.Calls.Single(c => c.File == expectedBinary);
        Assert.Contains(expectedLabelFlag, call.Args);
        Assert.Contains("FORTOS", call.Args);
        Assert.Equal("/dev/sda3", call.Args.Last());
        if (!string.IsNullOrEmpty(expectedFlag))
        {
            Assert.Contains(expectedFlag, call.Args);
        }
    }

    [Fact]
    public async Task Format_OmitsLabelWhenEmpty()
    {
        var runner = new FakeRunner();
        var tool = new MkfsTool(runner);

        await tool.FormatAsync("/dev/sda3", PartitionFs.Ext4, null, CancellationToken.None);

        var call = runner.Calls.Single(c => c.File == "mkfs.ext4");
        Assert.DoesNotContain("-L", call.Args);
    }
}
