using FortOS.Installer.Core.Tools;
using FortOS.Tests.Installer.Fakes;

namespace FortOS.Tests.Installer.Tools;

public class MdadmToolTests
{
    [Fact]
    public async Task CreateAsync_BuildsArrayArgs()
    {
        var runner = new FakeRunner();
        var tool = new MdadmTool(runner);

        await tool.CreateAsync("/dev/md127", 1, "fortos-data", ["/dev/sdb", "/dev/sdc"], CancellationToken.None);

        var call = runner.Calls.Single(c => c.File == "mdadm");
        Assert.Contains("--create", call.Args);
        Assert.Contains("/dev/md127", call.Args);
        Assert.Contains("--level=1", call.Args);
        Assert.Contains("--raid-devices=2", call.Args);
        Assert.Contains("--name=fortos-data", call.Args);
        Assert.Equal("/dev/sdb", call.Args[^2]);
        Assert.Equal("/dev/sdc", call.Args[^1]);
    }
}

public class CryptsetupToolTests
{
    [Fact]
    public async Task LuksFormat_SendsPassphraseViaStdin()
    {
        var runner = new FakeRunner();
        var tool = new CryptsetupTool(runner);

        await tool.LuksFormatAsync("/dev/sdb", "s3cret", CancellationToken.None);

        var call = runner.Calls.Single(c => c.File == "cryptsetup");
        Assert.Contains("luksFormat", call.Args);
        Assert.Contains("--type=luks2", call.Args);
        Assert.Contains("--batch-mode", call.Args);
        Assert.Contains("--key-file=-", call.Args);
        Assert.Equal("s3cret\n", call.StandardInput); // 口令不进命令行
        Assert.DoesNotContain(call.Args, a => a.Contains("s3cret"));
    }

    [Fact]
    public async Task LuksOpen_NamesMapperAndPassesStdin()
    {
        var runner = new FakeRunner();
        var tool = new CryptsetupTool(runner);

        await tool.LuksOpenAsync("/dev/sdb", "fortos-data", "s3cret", CancellationToken.None);

        var call = runner.Calls.Single(c => c.File == "cryptsetup");
        Assert.Equal(["open", "/dev/sdb", "fortos-data"], call.Args);
        Assert.Equal("s3cret\n", call.StandardInput);
    }
}
