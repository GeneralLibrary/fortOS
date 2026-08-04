using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FortOS.Installer.Core.Models;
using FortOS.Installer.Core.Session;
using FortOS.Installer.Core.Tools;

namespace FortOS.Installer.Gui.ViewModels;

/// <summary>
/// 主窗口 VM:组装向导页,负责把页面输入编译为 InstallConfig 并驱动执行。
/// 依赖通过工厂注入,便于无头测试。
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    public MainWindowViewModel(
        Func<LsblkTool>? lsblkFactory = null,
        Func<NmcliTool>? nmcliFactory = null,
        Func<InstallerSession>? sessionFactory = null,
        Action<Action>? uiDispatch = null)
    {
        var lsblk = (lsblkFactory ?? (() => new LsblkTool(new ProcessRunner())))();
        var nmcli = (nmcliFactory ?? (() => new NmcliTool(new ProcessRunner())))();

        Welcome = new WelcomeViewModel();
        DiskLayout = new DiskLayoutViewModel(lsblk);
        Network = new NetworkViewModel(nmcli);
        Account = new AccountViewModel();
        Install = new InstallViewModel(Welcome, DiskLayout, Network, Account, sessionFactory ?? (() => InstallerSession.CreateDefault()), uiDispatch);
        Complete = new CompleteViewModel();

        Wizard = new WizardViewModel([Welcome, DiskLayout, Network, Account, Install, Complete]);
        Install.Completed += () => Wizard.JumpTo(Wizard.PageCount - 1);

        ManagementAddress = Networking.NetworkInfo.ManagementUrl() ?? string.Empty;

        // 语言切换时刷新占位文案("No network"/"未检测到网络")。
        Localization.LocalizationService.Current.PropertyChanged += (_, _) => OnPropertyChanged(nameof(ManagementDisplay));
    }

    public WizardViewModel Wizard { get; }

    public WelcomeViewModel Welcome { get; }

    public DiskLayoutViewModel DiskLayout { get; }

    public NetworkViewModel Network { get; }

    public AccountViewModel Account { get; }

    public InstallViewModel Install { get; }

    public CompleteViewModel Complete { get; }

    /// <summary>左下角状态栏:FortOS 管理入口地址(空表示未检测到网络)。</summary>
    [ObservableProperty]
    private string _managementAddress = string.Empty;

    /// <summary>状态栏展示值:无网络时显示占位符。</summary>
    public string ManagementDisplay => string.IsNullOrEmpty(ManagementAddress) ? L["status.noNetwork"] : ManagementAddress;

    partial void OnManagementAddressChanged(string value) => OnPropertyChanged(nameof(ManagementDisplay));

    /// <summary>进入磁盘页时加载磁盘列表。</summary>
    [RelayCommand]
    private Task LoadDisksAsync() => DiskLayout.LoadAsync();

    /// <summary>安装页「开始安装」:校验后启动引擎(按钮已位于执行页,无需跳转)。</summary>
    [RelayCommand]
    private async Task BeginInstallAsync()
    {
        // 防御:进入执行前必须确认目标盘已选。
        if (DiskLayout.SelectedSystemDisk is null)
        {
            return;
        }
        await Install.StartCommand.ExecuteAsync(BuildInstallConfig());
    }

    /// <summary>把各页输入编译为引擎配置(与 install.yaml 相同的 schema)。</summary>
    public InstallConfig BuildInstallConfig() => new()
    {
        SystemDisk = DiskLayout.SelectedSystemDisk!.Path,
        RootFs = DiskLayout.RootFs,
        SwapMode = DiskLayout.SwapMode,
        SwapSizeMiB = DiskLayout.SwapMode == SwapMode.Fixed && long.TryParse(DiskLayout.SwapSizeMiB, out var size)
            ? size
            : null,
        Data = new DataDiskConfig
        {
            Mode = DiskLayout.DataMode,
            Disk = DiskLayout.DataMode == DataDiskMode.Single ? DiskLayout.SelectedDataDisk!.Path : null,
            FileSystem = DiskLayout.DataFs,
            Label = string.IsNullOrWhiteSpace(DiskLayout.DataLabel) ? "FORTOS_DATA" : DiskLayout.DataLabel.Trim(),
        },
        Network = new NetworkConfig
        {
            Mode = Network.Mode,
            Hostname = Network.Hostname.Trim(),
            Address = string.IsNullOrWhiteSpace(Network.Address) ? null : Network.Address.Trim(),
            Gateway = string.IsNullOrWhiteSpace(Network.Gateway) ? null : Network.Gateway.Trim(),
            Dns = Network.Dns
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray(),
        },
        Account = new AccountConfig
        {
            Username = Account.Username.Trim(),
            Password = Account.Password,
            SshPublicKey = Account.SshKey,
            Timezone = Account.Timezone,
        },
        Locale = new LocaleConfig
        {
            Language = Welcome.Language,
            Keyboard = Welcome.Keyboard,
        },
        Bootloader = BootloaderMode.Auto,
    };
}
