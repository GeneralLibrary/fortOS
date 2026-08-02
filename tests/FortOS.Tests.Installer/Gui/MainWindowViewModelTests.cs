using FortOS.Installer.Core.Logging;
using FortOS.Installer.Core.Models;
using FortOS.Installer.Core.Session;
using FortOS.Installer.Core.Steps;
using FortOS.Installer.Core.Tools;
using FortOS.Installer.Gui.ViewModels;
using FortOS.Tests.Installer.Fakes;

namespace FortOS.Tests.Installer.Gui;

[Collection("Gui.Localization")]
public class MainWindowViewModelTests
{
    private const string DisksJson = """
        {"blockdevices":[
          {"name":"vda","path":"/dev/vda","size":21474836480,"type":"disk","rota":0,"rm":0,"ro":0},
          {"name":"vdb","path":"/dev/vdb","size":107374182400,"type":"disk","rota":0,"rm":0,"ro":0}
        ]}
        """;

    private static FakeRunner Runner() => new() { StdoutResolver = (_, _) => DisksJson };

    private static async Task<MainWindowViewModel> CreateViewModelAsync(Func<InstallerSession>? sessionFactory = null)
    {
        var vm = new MainWindowViewModel(
            lsblkFactory: () => new LsblkTool(Runner()),
            sessionFactory: sessionFactory ?? (() => SuccessSession()),
            uiDispatch: action => action()); // 测试中同步执行,模拟 UI 线程
        await vm.DiskLayout.LoadAsync();
        return vm;
    }

    /// <summary>全成功的会话(fake steps)。</summary>
    private static InstallerSession SuccessSession() => new(
        [new OkStep()],
        new LsblkTool(Runner()),
        new RingLog());

    /// <summary>失败的会话(第一个步骤抛异常)。</summary>
    private static InstallerSession FailingSession() => new(
        [new FailingStep()],
        new LsblkTool(Runner()),
        new RingLog());

    private sealed class OkStep : IInstallStep
    {
        public string Name => "Ok";
        public InstallerPhase Phase => InstallerPhase.Partitioning;
        public Task ExecuteAsync(InstallContext context, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FailingStep : IInstallStep
    {
        public string Name => "Boom";
        public InstallerPhase Phase => InstallerPhase.Partitioning;
        public Task ExecuteAsync(InstallContext context, CancellationToken ct) => throw new InvalidOperationException("disk exploded");
    }

    private static void FillPages(MainWindowViewModel vm)
    {
        vm.DiskLayout.SelectedSystemDisk = vm.DiskLayout.Disks.First(d => d.Path == "/dev/vda");
        vm.Network.Hostname = "nas-test";
        vm.Account.Username = "admin";
        vm.Account.Password = "correct-horse";
        vm.Account.ConfirmPassword = "correct-horse";
    }

    [Fact]
    public async Task BuildInstallConfig_MapsAllPages()
    {
        var vm = await CreateViewModelAsync();
        vm.DiskLayout.SelectedSystemDisk = vm.DiskLayout.Disks.First(d => d.Path == "/dev/vda");
        vm.DiskLayout.DataMode = DataDiskMode.Single;
        vm.DiskLayout.SelectedDataDisk = vm.DiskLayout.Disks.First(d => d.Path == "/dev/vdb");
        vm.DiskLayout.DataFs = DataFileSystem.Xfs;
        vm.DiskLayout.DataLabel = "DATA";
        vm.DiskLayout.SwapMode = SwapMode.Fixed;
        vm.DiskLayout.SwapSizeMiB = "2048";
        vm.Network.Mode = NetworkMode.Static;
        vm.Network.Address = "192.168.1.10/24";
        vm.Network.Gateway = "192.168.1.1";
        vm.Network.Dns = "8.8.8.8, 1.1.1.1";
        vm.Account.Username = "admin";
        vm.Account.Password = "secret123";
        vm.Account.SshKey = "ssh-ed25519 AAAA test";
        vm.Account.Timezone = "Asia/Shanghai";
        vm.Welcome.Language = "zh_CN.UTF-8";

        var config = vm.BuildInstallConfig();

        Assert.Equal("/dev/vda", config.SystemDisk);
        Assert.Equal(RootFileSystem.Btrfs, config.RootFs);
        Assert.Equal(SwapMode.Fixed, config.SwapMode);
        Assert.Equal(2048, config.SwapSizeMiB);
        Assert.Equal(DataDiskMode.Single, config.Data.Mode);
        Assert.Equal("/dev/vdb", config.Data.Disk);
        Assert.Equal(DataFileSystem.Xfs, config.Data.FileSystem);
        Assert.Equal("DATA", config.Data.Label);
        Assert.Equal(NetworkMode.Static, config.Network.Mode);
        Assert.Equal("192.168.1.10/24", config.Network.Address);
        Assert.Equal(["8.8.8.8", "1.1.1.1"], config.Network.Dns);
        Assert.Equal("admin", config.Account.Username);
        Assert.Equal("secret123", config.Account.Password);
        Assert.Equal("Asia/Shanghai", config.Account.Timezone);
        Assert.Equal("zh_CN.UTF-8", config.Locale.Language);
        Assert.Equal(BootloaderMode.Auto, config.Bootloader);
    }

    [Fact]
    public async Task LoadDisksCommand_PopulatesDiskList()
    {
        // MainWindow code-behind 在 Opened 时触发该命令;验证命令确实填充磁盘列表。
        var vm = new MainWindowViewModel(
            lsblkFactory: () => new LsblkTool(Runner()),
            sessionFactory: SuccessSession,
            uiDispatch: action => action());

        await vm.LoadDisksCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.DiskLayout.Disks.Count);
    }

    [Fact]
    public async Task BeginInstall_WithoutSystemDisk_DoesNothing()
    {
        var vm = await CreateViewModelAsync();
        vm.Wizard.JumpTo(4); // Confirm(即便绕过校验链)

        await vm.BeginInstallCommand.ExecuteAsync(null);

        // 防御:未选系统盘时不前进、不执行。
        Assert.IsType<ConfirmViewModel>(vm.Wizard.CurrentPage);
    }

    [Fact]
    public async Task WizardWalkThrough_ReachesConfirmPage()
    {
        var vm = await CreateViewModelAsync();
        FillPages(vm);

        // Welcome(valid)→ 直接 Next 可达 Disk(选盘后 valid)→ Network(默认 valid)→ Account。
        Assert.True(vm.Wizard.CanGoNext);
        vm.Wizard.NextCommand.Execute(null);
        Assert.True(vm.Wizard.CanGoNext); // 磁盘页(已选盘)
        vm.Wizard.NextCommand.Execute(null);
        Assert.True(vm.Wizard.CanGoNext); // 网络页
        vm.Wizard.NextCommand.Execute(null);
        Assert.True(vm.Wizard.CanGoNext); // 账户页
        vm.Wizard.NextCommand.Execute(null);

        Assert.IsType<ConfirmViewModel>(vm.Wizard.CurrentPage);
    }

    [Fact]
    public async Task BeginInstall_OnSuccess_JumpsToCompletePage()
    {
        var vm = await CreateViewModelAsync();
        FillPages(vm);

        vm.Wizard.JumpTo(4); // Confirm
        await vm.BeginInstallCommand.ExecuteAsync(null);

        Assert.IsType<CompleteViewModel>(vm.Wizard.CurrentPage);
        Assert.False(vm.Install.IsFailed);
        Assert.Equal(100, vm.Install.Progress);
    }

    [Fact]
    public async Task BeginInstall_OnFailure_StaysOnInstallPageAndReportsError()
    {
        var vm = await CreateViewModelAsync(sessionFactory: FailingSession);
        FillPages(vm);

        vm.Wizard.JumpTo(4);
        await vm.BeginInstallCommand.ExecuteAsync(null);

        Assert.IsType<InstallViewModel>(vm.Wizard.CurrentPage);
        Assert.True(vm.Install.IsFailed);
        Assert.Contains("Boom", vm.Install.ErrorMessage);
        Assert.NotEmpty(vm.Install.LogLines); // 日志已收集
    }

    [Fact]
    public async Task BeginInstall_ConfigValidationError_TurnsIntoFailureNotCrash()
    {
        // 引擎的 ValidateConfig 在 RunAsync 的 try 块外抛出(设计使然);
        // GUI 必须把它兜底为失败状态,而不是卡在执行页或崩溃。
        var vm = await CreateViewModelAsync();
        FillPages(vm);
        vm.Account.Timezone = "../../etc/passwd"; // 非法时区,引擎校验会抛 ConfigException

        vm.Wizard.JumpTo(4);
        await vm.BeginInstallCommand.ExecuteAsync(null);

        Assert.IsType<InstallViewModel>(vm.Wizard.CurrentPage);
        Assert.True(vm.Install.IsFailed);
        Assert.Contains("timezone", vm.Install.ErrorMessage);
    }

    [Fact]
    public async Task InstallViewModel_Retry_AfterFailureRestarts()
    {
        // 第一次失败,第二次成功:用可变步骤。
        var attempts = 0;
        var session = new InstallerSession(
            [new CountingStep(() => ++attempts == 1)],
            new LsblkTool(Runner()),
            new RingLog());

        var vm = new InstallViewModel(() => session, action => action());
        var config = new InstallConfig { SystemDisk = "/dev/vda", Account = new AccountConfig { Username = "admin" } };

        await vm.StartCommand.ExecuteAsync(config);
        Assert.True(vm.IsFailed);

        vm.RetryCommand.Execute(null);
        Assert.False(vm.IsFailed);
        Assert.Equal(2, attempts);
    }

    private sealed class CountingStep(Func<bool> failOnFirstAttempt) : IInstallStep
    {
        public string Name => "Counted";
        public InstallerPhase Phase => InstallerPhase.Partitioning;

        public Task ExecuteAsync(InstallContext context, CancellationToken ct)
        {
            if (failOnFirstAttempt())
            {
                throw new InvalidOperationException("transient failure");
            }
            return Task.CompletedTask;
        }
    }
}
