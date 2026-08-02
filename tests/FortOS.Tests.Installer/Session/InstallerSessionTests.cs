using FortOS.Installer.Core.Logging;
using FortOS.Installer.Core.Models;
using FortOS.Installer.Core.Session;
using FortOS.Installer.Core.Steps;
using FortOS.Installer.Core.Tools;
using FortOS.Tests.Installer.Fakes;

namespace FortOS.Tests.Installer.Session;

public class InstallerSessionTests
{
    private const string DisksJson = """
        {"blockdevices":[{"name":"vda","path":"/dev/vda","size":21474836480,"type":"disk","rota":0,"rm":0,"ro":0}]}
        """;

    private static readonly InstallConfig Config = new()
    {
        SystemDisk = "/dev/vda",
        RootFs = RootFileSystem.Btrfs,
        Network = new NetworkConfig { Hostname = "fortos" },
        Account = new AccountConfig { Username = "admin", Password = "secret" },
    };

    private sealed class FakeStep(string name, InstallerPhase phase, Action? onExecute = null) : IInstallStep
    {
        public string Name => name;
        public InstallerPhase Phase => phase;
        public int ExecuteCount { get; private set; }

        public Task ExecuteAsync(InstallContext context, CancellationToken ct)
        {
            ExecuteCount++;
            onExecute?.Invoke();
            return Task.CompletedTask;
        }
    }

    private static (InstallerSession Session, List<InstallerPhase> Phases, FakeRunner Runner) CreateSession(IEnumerable<IInstallStep> steps, string disksJson = DisksJson)
    {
        var runner = new FakeRunner { StdoutResolver = (_, _) => disksJson };
        var session = new InstallerSession(steps, new LsblkTool(runner), new RingLog());
        var phases = new List<InstallerPhase>();
        session.PhaseChanged += phases.Add;
        return (session, phases, runner);
    }

    [Fact]
    public async Task RunAsync_ExecutesAllStepsInOrderAndSucceeds()
    {
        var step1 = new FakeStep("A", InstallerPhase.Partitioning);
        var step2 = new FakeStep("B", InstallerPhase.Formatting);
        var (session, phases, _) = CreateSession([step1, step2]);

        var result = await session.RunAsync(Config, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(InstallerPhase.Done, session.Phase);
        Assert.Equal(1, step1.ExecuteCount);
        Assert.Equal(1, step2.ExecuteCount);
        Assert.Equal(
            [InstallerPhase.CollectInfo, InstallerPhase.Partitioning, InstallerPhase.Formatting, InstallerPhase.Done],
            phases);
    }

    [Fact]
    public async Task RunAsync_FailureStopsAndReportsFailedStep()
    {
        var step1 = new FakeStep("A", InstallerPhase.Partitioning);
        var step2 = new FakeStep("B", InstallerPhase.Formatting, () => throw new InvalidOperationException("boom"));
        var step3 = new FakeStep("C", InstallerPhase.Copying);
        var (session, _, _) = CreateSession([step1, step2, step3]);

        var result = await session.RunAsync(Config, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("B", result.FailedStep);
        Assert.Equal(InstallerPhase.Failed, session.Phase);
        Assert.Equal(0, step3.ExecuteCount); // 后续步骤不执行
    }

    [Fact]
    public async Task RunAsync_MissingSystemDisk_FailsAtCollectInfo()
    {
        var step1 = new FakeStep("A", InstallerPhase.Partitioning);
        var (session, _, _) = CreateSession([step1], disksJson: """{"blockdevices":[]}""");

        var result = await session.RunAsync(Config, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.FailedStep); // 失败发生在任何步骤之前
        Assert.Contains("was not found", result.ErrorMessage);
    }

    [Fact]
    public async Task RunAsync_DataDiskSameAsSystemDisk_Fails()
    {
        var step1 = new FakeStep("A", InstallerPhase.Partitioning);
        var configWithData = new InstallConfig
        {
            SystemDisk = "/dev/vda",
            Data = new DataDiskConfig { Mode = DataDiskMode.Single, Disk = "/dev/vda" },
            Account = new AccountConfig { Username = "admin" },
        };
        var (session, _, _) = CreateSession([step1]);

        var result = await session.RunAsync(configWithData, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("must be different", result.ErrorMessage);
    }

    [Fact]
    public async Task RunAsync_InvalidConfig_ThrowsBeforeExecution()
    {
        var step1 = new FakeStep("A", InstallerPhase.Partitioning);
        var (session, _, _) = CreateSession([step1]);
        var bad = new InstallConfig { SystemDisk = "" };

        await Assert.ThrowsAsync<FortOS.Installer.Core.Exceptions.ConfigException>(() => session.RunAsync(bad, CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_RaidWithSingleDisk_Rejected()
    {
        var step1 = new FakeStep("A", InstallerPhase.Partitioning);
        var (session, _, _) = CreateSession([step1]);
        var config = new InstallConfig
        {
            SystemDisk = "/dev/vda",
            Data = new DataDiskConfig { Mode = DataDiskMode.Raid, RaidDisks = ["/dev/vdb"] },
            Account = new AccountConfig { Username = "admin" },
        };

        await Assert.ThrowsAsync<FortOS.Installer.Core.Exceptions.ConfigException>(() => session.RunAsync(config, CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_LuksWithoutPassphrase_Rejected()
    {
        var step1 = new FakeStep("A", InstallerPhase.Partitioning);
        var (session, _, _) = CreateSession([step1]);
        var config = new InstallConfig
        {
            SystemDisk = "/dev/vda",
            Data = new DataDiskConfig { Mode = DataDiskMode.Luks, Disk = "/dev/vdb" },
            Account = new AccountConfig { Username = "admin" },
        };

        await Assert.ThrowsAsync<FortOS.Installer.Core.Exceptions.ConfigException>(() => session.RunAsync(config, CancellationToken.None));
    }

    [Theory]
    [InlineData(5, 2)]   // RAID5 需要 ≥3
    [InlineData(10, 3)]  // RAID10 需要 ≥4
    [InlineData(2, 4)]   // 非法级别
    public async Task RunAsync_RaidLevelRequiresEnoughDisks_Rejected(int level, int diskCount)
    {
        var step1 = new FakeStep("A", InstallerPhase.Partitioning);
        var (session, _, _) = CreateSession([step1]);
        var members = Enumerable.Range(0, diskCount).Select(i => $"/dev/sd{(char)('b' + i)}").ToArray();
        var config = new InstallConfig
        {
            SystemDisk = "/dev/vda",
            Data = new DataDiskConfig { Mode = DataDiskMode.Raid, RaidLevel = level, RaidDisks = members },
            Account = new AccountConfig { Username = "admin" },
        };

        await Assert.ThrowsAsync<FortOS.Installer.Core.Exceptions.ConfigException>(() => session.RunAsync(config, CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_RaidMemberEqualsSystemDisk_Rejected()
    {
        var step1 = new FakeStep("A", InstallerPhase.Partitioning);
        var (session, _, _) = CreateSession([step1]);
        var config = new InstallConfig
        {
            SystemDisk = "/dev/vda",
            Data = new DataDiskConfig { Mode = DataDiskMode.Raid, RaidDisks = ["/dev/vda", "/dev/vdb"] },
            Account = new AccountConfig { Username = "admin" },
        };

        var result = await session.RunAsync(config, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("must be different from system.disk", result.ErrorMessage);
    }

    [Fact]
    public async Task RunAsync_RaidDuplicateMembers_Rejected()
    {
        const string threeDisks = """
            {"blockdevices":[
              {"name":"vda","path":"/dev/vda","size":21474836480,"type":"disk","rota":0,"rm":0,"ro":0},
              {"name":"vdb","path":"/dev/vdb","size":107374182400,"type":"disk","rota":0,"rm":0,"ro":0},
              {"name":"vdc","path":"/dev/vdc","size":107374182400,"type":"disk","rota":0,"rm":0,"ro":0}
            ]}
            """;
        var step1 = new FakeStep("A", InstallerPhase.Partitioning);
        var (session, _, _) = CreateSession([step1], disksJson: threeDisks);
        var config = new InstallConfig
        {
            SystemDisk = "/dev/vda",
            Data = new DataDiskConfig { Mode = DataDiskMode.Raid, RaidDisks = ["/dev/vdb", "/dev/vdc", "/dev/vdb"] },
            Account = new AccountConfig { Username = "admin" },
        };

        var result = await session.RunAsync(config, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("duplicate", result.ErrorMessage);
    }

    [Theory]
    [InlineData("../../evil")]
    [InlineData("md 127")]
    [InlineData("md127;rm")]
    public async Task RunAsync_UnsafeDeviceNames_Rejected(string badName)
    {
        var step1 = new FakeStep("A", InstallerPhase.Partitioning);
        var (session, _, _) = CreateSession([step1]);
        var config = new InstallConfig
        {
            SystemDisk = "/dev/vda",
            Data = new DataDiskConfig
            {
                Mode = DataDiskMode.Raid,
                RaidDisks = ["/dev/vdb", "/dev/vdc"],
                RaidDeviceName = badName,
            },
            Account = new AccountConfig { Username = "admin" },
        };

        await Assert.ThrowsAsync<FortOS.Installer.Core.Exceptions.ConfigException>(() => session.RunAsync(config, CancellationToken.None));
    }

    [Theory]
    [InlineData("pass:with-colon")]
    [InlineData("pass\nwith-newline")]
    public async Task RunAsync_PasswordWithSeparator_Rejected(string password)
    {
        var step1 = new FakeStep("A", InstallerPhase.Partitioning);
        var (session, _, _) = CreateSession([step1]);
        var config = new InstallConfig
        {
            SystemDisk = "/dev/vda",
            Account = new AccountConfig { Username = "admin", Password = password },
        };

        await Assert.ThrowsAsync<FortOS.Installer.Core.Exceptions.ConfigException>(() => session.RunAsync(config, CancellationToken.None));
    }

    [Theory]
    [InlineData("DATA LABEL")]
    [InlineData("a/../b")]
    public async Task RunAsync_UnsafeDataLabel_Rejected(string label)
    {
        var step1 = new FakeStep("A", InstallerPhase.Partitioning);
        var (session, _, _) = CreateSession([step1]);
        var config = new InstallConfig
        {
            SystemDisk = "/dev/vda",
            Data = new DataDiskConfig { Mode = DataDiskMode.Single, Disk = "/dev/vdb", Label = label },
            Account = new AccountConfig { Username = "admin" },
        };

        await Assert.ThrowsAsync<FortOS.Installer.Core.Exceptions.ConfigException>(() => session.RunAsync(config, CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_StaticNetworkWithoutCidr_Rejected()
    {
        var step1 = new FakeStep("A", InstallerPhase.Partitioning);
        var (session, _, _) = CreateSession([step1]);
        var config = new InstallConfig
        {
            SystemDisk = "/dev/vda",
            Network = new NetworkConfig { Mode = NetworkMode.Static, Address = "192.168.1.10" },
            Account = new AccountConfig { Username = "admin" },
        };

        await Assert.ThrowsAsync<FortOS.Installer.Core.Exceptions.ConfigException>(() => session.RunAsync(config, CancellationToken.None));
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("root")]
    [InlineData("admin-1")]
    [InlineData("_svc")]
    public async Task RunAsync_ValidUsernames_Accepted(string username)
    {
        var step1 = new FakeStep("A", InstallerPhase.Partitioning);
        var (session, _, _) = CreateSession([step1]);
        var config = new InstallConfig
        {
            SystemDisk = "/dev/vda",
            Account = new AccountConfig { Username = username },
        };

        var result = await session.RunAsync(config, CancellationToken.None);

        Assert.True(result.Success);
    }

    [Theory]
    [InlineData("admin;rm -rf /")]
    [InlineData("a b")]
    [InlineData("x'--y")]
    [InlineData("")]          // 空
    [InlineData("UPPER")]
    [InlineData("a..b")]
    public async Task RunAsync_UnsafeUsernames_Rejected(string username)
    {
        var step1 = new FakeStep("A", InstallerPhase.Partitioning);
        var (session, _, _) = CreateSession([step1]);
        var config = new InstallConfig
        {
            SystemDisk = "/dev/vda",
            Account = new AccountConfig { Username = username },
        };

        await Assert.ThrowsAsync<FortOS.Installer.Core.Exceptions.ConfigException>(() => session.RunAsync(config, CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_Failure_InvokesCleanup()
    {
        var step1 = new FakeStep("A", InstallerPhase.Partitioning, () => throw new InvalidOperationException("boom"));
        var cleanupCalled = false;
        var runner = new FakeRunner { StdoutResolver = (_, _) => DisksJson };
        var session = new InstallerSession(
            [step1],
            new LsblkTool(runner),
            cleanupTarget: (_, _) =>
            {
                cleanupCalled = true;
                return Task.CompletedTask;
            });

        var result = await session.RunAsync(Config, CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(cleanupCalled, "cleanup must run on failure");
    }

    [Fact]
    public void IsDiskInUse_DetectsMountedPartitions()
    {
        const string mounts = """
            overlay / overlay rw 0 0
            /dev/vda2 /media/foo ext4 rw 0 0
            tmpfs /run tmpfs rw 0 0
            """;

        Assert.True(InstallerSession.IsDiskInUse("/dev/vda", mounts));
        Assert.False(InstallerSession.IsDiskInUse("/dev/vdb", mounts));
    }
}
