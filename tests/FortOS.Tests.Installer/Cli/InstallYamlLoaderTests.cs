using FortOS.Installer.Cli;
using FortOS.Installer.Core.Exceptions;
using FortOS.Installer.Core.Models;

namespace FortOS.Tests.Installer.Cli;

public class InstallYamlLoaderTests
{
    private static InstallConfig Load(string yaml)
    {
        var path = Path.Combine(Path.GetTempPath(), $"fortos-install-test-{Guid.NewGuid():N}.yaml");
        try
        {
            File.WriteAllText(path, yaml);
            return InstallYamlLoader.ToConfig(InstallYamlLoader.LoadYaml(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ToConfig_MapsFullYaml()
    {
        var config = Load("""
            system:
              disk: /dev/sda
              rootFs: ext4
              swap: 2048
            data:
              mode: single
              disk: /dev/sdb
              fs: xfs
              label: DATA
            network:
              mode: static
              hostname: nas1
              address: 192.168.1.10/24
              gateway: 192.168.1.1
              dns: [8.8.8.8, 1.1.1.1]
            account:
              username: admin
              password: hunter2
              timezone: Asia/Shanghai
            locale:
              lang: zh_CN.UTF-8
              keyboard: us
            bootloader: uefi
            """);

        Assert.Equal("/dev/sda", config.SystemDisk);
        Assert.Equal(RootFileSystem.Ext4, config.RootFs);
        Assert.Equal(SwapMode.Fixed, config.SwapMode);
        Assert.Equal(2048, config.SwapSizeMiB);
        Assert.Equal(DataDiskMode.Single, config.Data.Mode);
        Assert.Equal("/dev/sdb", config.Data.Disk);
        Assert.Equal(DataFileSystem.Xfs, config.Data.FileSystem);
        Assert.Equal("DATA", config.Data.Label);
        Assert.Equal(NetworkMode.Static, config.Network.Mode);
        Assert.Equal("nas1", config.Network.Hostname);
        Assert.Equal("192.168.1.10/24", config.Network.Address);
        Assert.Equal(["8.8.8.8", "1.1.1.1"], config.Network.Dns);
        Assert.Equal("admin", config.Account.Username);
        Assert.Equal("Asia/Shanghai", config.Account.Timezone);
        Assert.Equal("zh_CN.UTF-8", config.Locale.Language);
        Assert.Equal(BootloaderMode.Uefi, config.Bootloader);
    }

    [Fact]
    public void ToConfig_DefaultsWhenOmitted()
    {
        var config = Load("""
            system:
              disk: /dev/sda
            account:
              username: admin
            """);

        Assert.Equal(RootFileSystem.Btrfs, config.RootFs);
        Assert.Equal(SwapMode.Auto, config.SwapMode);
        Assert.Equal(DataDiskMode.None, config.Data.Mode);
        Assert.Equal(NetworkMode.Dhcp, config.Network.Mode);
        Assert.Equal("fortos", config.Network.Hostname);
        Assert.Equal(BootloaderMode.Auto, config.Bootloader);
    }

    [Fact]
    public void ToConfig_MissingSystemDisk_Throws()
    {
        Assert.Throws<ConfigException>(() => Load("account:\n  username: admin\n"));
    }

    [Fact]
    public void ToConfig_InvalidEnum_Throws()
    {
        Assert.Throws<ConfigException>(() => Load("""
            system:
              disk: /dev/sda
              rootFs: zfs
            account:
              username: admin
            """));
    }

    [Fact]
    public void ToConfig_InvalidYaml_Throws()
    {
        Assert.Throws<ConfigException>(() => Load("system: [unclosed"));
    }

    [Fact]
    public void ToConfig_RaidMode_MapsMembers()
    {
        var config = Load("""
            system:
              disk: /dev/sda
            data:
              mode: raid
              raidLevel: 1
              raidDisks: [/dev/sdb, /dev/sdc]
              fs: xfs
            account:
              username: admin
            """);

        Assert.Equal(DataDiskMode.Raid, config.Data.Mode);
        Assert.Equal(1, config.Data.RaidLevel);
        Assert.Equal(["/dev/sdb", "/dev/sdc"], config.Data.RaidDisks);
        Assert.Equal("/dev/md127", $"/dev/{config.Data.RaidDeviceName}");
        Assert.Equal(DataFileSystem.Xfs, config.Data.FileSystem);
    }

    [Fact]
    public void ToConfig_LuksMode_MapsPassphraseAndMapper()
    {
        var config = Load("""
            system:
              disk: /dev/sda
            data:
              mode: luks
              disk: /dev/sdb
              luksPassphrase: topsecret
              luksMapperName: nas-data
            account:
              username: admin
            """);

        Assert.Equal(DataDiskMode.Luks, config.Data.Mode);
        Assert.Equal("/dev/sdb", config.Data.Disk);
        Assert.Equal("topsecret", config.Data.LuksPassphrase);
        Assert.Equal("nas-data", config.Data.LuksMapperName);
    }

    [Fact]
    public void ToConfig_NullOptionalSections_UseDefaults()
    {
        // "data:" 空节相当于省略:可选节回落默认,必填节(system/account)缺失才报错。
        var config = Load("""
            system:
              disk: /dev/sda
            data:
            account:
              username: admin
            """);

        Assert.Equal(DataDiskMode.None, config.Data.Mode);
        Assert.Equal(NetworkMode.Dhcp, config.Network.Mode);
    }

    [Fact]
    public void ToConfig_MissingSystemSection_Throws()
        => Assert.Throws<ConfigException>(() => Load("account:\n  username: admin\n"));

    [Theory]
    [InlineData("8G")]
    [InlineData("4GiB")]
    [InlineData("-1024")]
    public void ToConfig_InvalidSwapValue_Throws(string swap)
    {
        Assert.Throws<ConfigException>(() => Load($"""
            system:
              disk: /dev/sda
              swap: {swap}
            account:
              username: admin
            """));
    }

    [Fact]
    public void ToConfig_NumericEnumValue_Rejected()
    {
        // data.mode: 1 不应静默映射成枚举值,必须报错。
        Assert.Throws<ConfigException>(() => Load("""
            system:
              disk: /dev/sda
            data:
              mode: 1
            account:
              username: admin
            """));
    }
}
