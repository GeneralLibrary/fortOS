using FortOS.Core;
using FortOS.Platform.Linux;

namespace FortOS.Tests.Integration.Platform;

/// <summary>
/// F1 数据保护回归测试：创建分区前必须确认磁盘尚无分区表才初始化 GPT 标签。
/// 判定逻辑抽为 ShouldInitializeDiskLabel 纯函数，锁定「已有标签绝不 mklabel」、
/// 「非标签类错误（设备不存在等）拒绝破坏性操作」两条语义。
/// </summary>
public sealed class LinuxDiskManagerTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldInitializeDiskLabel_ExistingLabel_ReturnsFalse()
    {
        // parted print 成功（退出码 0）：磁盘已有有效标签，绝不重新 mklabel。
        var probe = new CommandResult { ExitCode = 0, Stdout = "BYT;\n/dev/sda:100GB:scsi:512:512:gpt:ATA Disk:;" };
        Assert.False(LinuxDiskManager.ShouldInitializeDiskLabel(probe));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldInitializeDiskLabel_UnrecognisedLabel_ReturnsTrue()
    {
        // 全新盘：parted 明确报告 unrecognised disk label，允许初始化 GPT 标签。
        var probe = new CommandResult { ExitCode = 1, Stderr = "Error: /dev/sdb: unrecognised disk label" };
        Assert.True(LinuxDiskManager.ShouldInitializeDiskLabel(probe));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldInitializeDiskLabel_OtherError_ReturnsFalse()
    {
        // 设备不存在/IO 失败等非标签类错误：禁止 mklabel（防止对异常设备做破坏性操作）。
        var probe = new CommandResult { ExitCode = 1, Stderr = "Error: /dev/sdz: No such file or directory" };
        Assert.False(LinuxDiskManager.ShouldInitializeDiskLabel(probe));
    }
}
