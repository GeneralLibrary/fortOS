using FortOS.Installer.Core.Models;
using FortOS.Installer.Core.Steps;

namespace FortOS.Tests.Installer.Steps;

public class PartitionStepTests
{
    [Theory]
    [InlineData("/dev/sda", 1, "/dev/sda1")]
    [InlineData("/dev/sda", 3, "/dev/sda3")]
    [InlineData("/dev/nvme0n1", 1, "/dev/nvme0n1p1")]
    [InlineData("/dev/mmcblk0", 2, "/dev/mmcblk0p2")]
    [InlineData("/dev/vda", 1, "/dev/vda1")]
    [InlineData("/dev/loop0", 1, "/dev/loop0p1")]
    public void PartitionDevice_HandlesNamespacing(string disk, int number, string expected)
    {
        Assert.Equal(expected, PartitionStep.PartitionDevice(disk, number));
    }

    [Fact]
    public void ResolveSwapMiB_Off_IsZero() => Assert.Equal(0, PartitionStep.ResolveSwapMiB(new InstallConfig { SystemDisk = "/dev/sda", SwapMode = SwapMode.Off }));

    [Fact]
    public void ResolveSwapMiB_Fixed_UsesConfiguredSize() =>
        Assert.Equal(2048, PartitionStep.ResolveSwapMiB(new InstallConfig { SystemDisk = "/dev/sda", SwapMode = SwapMode.Fixed, SwapSizeMiB = 2048 }));

    [Fact]
    public void ResolveSwapMiB_Auto_IsPositive()
    {
        // Auto 读取 /proc/meminfo;任何主机上都应得到 > 0 的值。
        Assert.True(PartitionStep.ResolveSwapMiB(new InstallConfig { SystemDisk = "/dev/sda" }) > 0);
    }

    [Theory]
    [InlineData(BootloaderMode.Uefi, BootModeKind.Uefi)]
    [InlineData(BootloaderMode.Bios, BootModeKind.Bios)]
    public void ResolveBootMode_ExplicitConfigWins(BootloaderMode mode, BootModeKind expected)
        => Assert.Equal(expected, PartitionStep.ResolveBootMode(mode));
}

public class PartitionTemplatesTests
{
    [Fact]
    public void SystemDefault_NoSwap_ThreePartitionsRootLast()
    {
        var specs = PartitionTemplates.SystemDefault(0);

        Assert.Equal(3, specs.Count);
        Assert.Equal(("ef02", 1L), (specs[0].TypeCode, specs[0].SizeMiB));
        Assert.Equal("ef00", specs[1].TypeCode);
        Assert.Equal("8304", specs[2].TypeCode);
        Assert.Equal(0, specs[2].SizeMiB); // 根分区收尾
    }

    [Fact]
    public void SystemDefault_WithSwap_SwapBeforeRoot()
    {
        var specs = PartitionTemplates.SystemDefault(4096);

        Assert.Equal(4, specs.Count);
        Assert.Equal("8200", specs[2].TypeCode);
        Assert.Equal(4096, specs[2].SizeMiB);
        Assert.Equal("8304", specs[3].TypeCode);
        Assert.Equal(0, specs[3].SizeMiB);
    }
}
