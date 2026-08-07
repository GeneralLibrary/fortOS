using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FortOS.Installer.Core.Models;
using FortOS.Installer.Core.Session;
using FortOS.Installer.Core.Tools;

namespace FortOS.Installer.Gui.ViewModels;

/// <summary>
/// Main window VM: assembles the wizard pages, compiles page input into InstallConfig, and drives execution.
/// Dependencies are injected via factories, making headless testing easy.
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

        // Refresh the placeholder text on language switch ("No network" / "No network detected").
        Localization.LocalizationService.Current.PropertyChanged += (_, _) => OnPropertyChanged(nameof(ManagementDisplay));
    }

    public WizardViewModel Wizard { get; }

    public WelcomeViewModel Welcome { get; }

    public DiskLayoutViewModel DiskLayout { get; }

    public NetworkViewModel Network { get; }

    public AccountViewModel Account { get; }

    public InstallViewModel Install { get; }

    public CompleteViewModel Complete { get; }

    /// <summary>Bottom-left status bar: FortOS management address (empty means no network detected).</summary>
    [ObservableProperty]
    private string _managementAddress = string.Empty;

    /// <summary>Status bar display value: shows a placeholder when there is no network.</summary>
    public string ManagementDisplay => string.IsNullOrEmpty(ManagementAddress) ? L["status.noNetwork"] : ManagementAddress;

    partial void OnManagementAddressChanged(string value) => OnPropertyChanged(nameof(ManagementDisplay));

    /// <summary>Loads the disk list when entering the disk page.</summary>
    [RelayCommand]
    private Task LoadDisksAsync() => DiskLayout.LoadAsync();

    /// <summary>Install page "Begin installation": validates then starts the engine (the button is already on the execution page, no navigation needed).</summary>
    [RelayCommand]
    private async Task BeginInstallAsync()
    {
        // Defense: the target disk must be confirmed selected before entering execution.
        if (DiskLayout.SelectedSystemDisk is null)
        {
            return;
        }
        await Install.StartCommand.ExecuteAsync(BuildInstallConfig());
    }

    /// <summary>Compiles each page's input into the engine configuration (same schema as install.yaml).</summary>
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
