using CommunityToolkit.Mvvm.ComponentModel;
using FortOS.Installer.Core.Models;

namespace FortOS.Installer.Gui.ViewModels;

/// <summary>第 3 页:网络配置(DHCP 默认,静态可选)。</summary>
public partial class NetworkViewModel : ViewModelBase, IWizardPage
{
    public override string Title => "Network";

    public IReadOnlyList<NetworkMode> ModeOptions { get; } = Enum.GetValues<NetworkMode>();

    [ObservableProperty]
    private NetworkMode _mode = NetworkMode.Dhcp;

    [ObservableProperty]
    private string _hostname = "fortos";

    [ObservableProperty]
    private string _address = string.Empty;

    [ObservableProperty]
    private string _gateway = string.Empty;

    [ObservableProperty]
    private string _dns = string.Empty;

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Hostname) &&
        (Mode == NetworkMode.Dhcp || !string.IsNullOrWhiteSpace(Address));

    /// <summary>静态模式时显示 IP/网关/DNS 输入。</summary>
    public bool ShowStaticFields => Mode == NetworkMode.Static;

    partial void OnModeChanged(NetworkMode value)
    {
        OnPropertyChanged(nameof(ShowStaticFields));
        RaiseIsValidChanged();
    }

    partial void OnHostnameChanged(string value) => RaiseIsValidChanged();

    partial void OnAddressChanged(string value) => RaiseIsValidChanged();
}
