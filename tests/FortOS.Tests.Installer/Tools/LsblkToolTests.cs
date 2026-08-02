using FortOS.Installer.Core.Tools;
using FortOS.Tests.Installer.Fakes;

namespace FortOS.Tests.Installer.Tools;

public class LsblkToolTests
{
    private const string TwoDisksJson = """
        {
          "blockdevices": [
            {"name":"sda","path":"/dev/sda","size":21474836480,"model":"QEMU HARDDISK","serial":"","tran":"sata","rota":1,"rm":0,"ro":0,"type":"disk","uuid":null},
            {"name":"sda1","path":"/dev/sda1","size":1048576,"model":null,"serial":null,"tran":null,"rota":null,"rm":null,"ro":null,"type":"part","uuid":"1234-ABCD"},
            {"name":"nvme0n1","path":"/dev/nvme0n1","size":107374182400,"model":"Samsung SSD","serial":"S123","tran":"nvme","rota":0,"rm":0,"ro":0,"type":"disk","uuid":null}
          ]
        }
        """;

    [Fact]
    public async Task ListDisks_ParsesAndFiltersPartitions()
    {
        var runner = new FakeRunner { StdoutResolver = (_, _) => TwoDisksJson };
        var tool = new LsblkTool(runner);

        var disks = await tool.ListDisksAsync(CancellationToken.None);

        Assert.Equal(2, disks.Count);
        var sda = disks[0];
        Assert.Equal("sda", sda.Name);
        Assert.Equal("/dev/sda", sda.Path);
        Assert.Equal(21_474_836_480UL, sda.SizeBytes);
        Assert.True(sda.IsRotational);
        Assert.False(sda.IsRemovable);
        Assert.Equal("sata", sda.Transport);
        Assert.Equal("21.5 GB", sda.SizeHuman);
    }

    [Fact]
    public async Task ListDisks_IncludesLoopDevices()
    {
        // WSL/QEMU 场景:loop 虚拟盘(type=loop)也是合法安装目标。
        const string loopJson = """
            {"blockdevices":[
              {"name":"loop0","path":"/dev/loop0","size":21474836480,"type":"loop","rota":0,"rm":0,"ro":0},
              {"name":"sda","path":"/dev/sda","size":21474836480,"type":"disk","rota":0,"rm":0,"ro":0},
              {"name":"sda1","path":"/dev/sda1","size":1048576,"type":"part"}
            ]}
            """;
        var runner = new FakeRunner { StdoutResolver = (_, _) => loopJson };
        var tool = new LsblkTool(runner);

        var disks = await tool.ListDisksAsync(CancellationToken.None);

        Assert.Equal(2, disks.Count);
        Assert.Contains(disks, d => d.Path == "/dev/loop0");
        Assert.DoesNotContain(disks, d => d.Name == "sda1");
    }

    [Fact]
    public async Task ListDisks_UsesFixedJsonFieldSet()
    {
        var runner = new FakeRunner { StdoutResolver = (_, _) => TwoDisksJson };
        var tool = new LsblkTool(runner);

        await tool.ListDisksAsync(CancellationToken.None);

        var call = runner.Calls.Single(c => c.File == "lsblk");
        Assert.Contains("--json", call.Args);
        Assert.Contains("-b", call.Args);
        Assert.Contains("-o", call.Args);
        Assert.Contains("NAME,PATH,SIZE,MODEL,SERIAL,TRAN,ROTA,RM,RO,TYPE", call.Args);
        // UUID 由 BlkidTool 负责(lsblk 的 uuid 列在 loop 设备上可能不刷新)。
        Assert.DoesNotContain("UUID", call.Args);
    }
}

/// <summary>blkid 适配器测试(UUID 读取已从 LsblkTool 拆分为 BlkidTool)。</summary>
public class BlkidToolTests
{
    [Fact]
    public async Task GetUuid_ReturnsUuidForDevice()
    {
        var runner = new FakeRunner
        {
            StdoutResolver = (file, args) =>
                file == "blkid" && args.Contains("/dev/sda1") ? "1234-ABCD\n" : string.Empty,
        };
        var tool = new BlkidTool(runner);

        var uuid = await tool.GetUuidAsync("/dev/sda1", CancellationToken.None);

        Assert.Equal("1234-ABCD", uuid);
    }

    [Fact]
    public async Task GetUuid_ReturnsNullWhenNoFilesystem()
    {
        var runner = new FakeRunner { ExitCode = 2 }; // blkid 对空分区返回非零
        var tool = new BlkidTool(runner);

        var uuid = await tool.GetUuidAsync("/dev/sda1", CancellationToken.None);

        Assert.Null(uuid);
    }
}
