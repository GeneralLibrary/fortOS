using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FortOS.Installer.Core.Models;
using FortOS.Installer.Core.Tools;

namespace FortOS.Installer.Gui.ViewModels;

/// <summary>
/// 第 3 页:网络配置。DHCP 为默认;可选静态地址;
/// 无线网络经 nmcli(NetworkManager)扫描与连接,连接成功后由 DHCP 获取地址。
/// </summary>
public partial class NetworkViewModel : ViewModelBase, IWizardPage
{
    private readonly NmcliTool _nmcli;

    public NetworkViewModel(NmcliTool? nmcli = null)
    {
        _nmcli = nmcli ?? new NmcliTool(new ProcessRunner());
    }

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

    // ---- WiFi(nmcli)----

    public ObservableCollection<string> WifiSsids { get; } = [];

    [ObservableProperty]
    private string? _selectedSsid;

    [ObservableProperty]
    private string _wifiPassword = string.Empty;

    [ObservableProperty]
    private string _wifiStatus = string.Empty;

    [ObservableProperty]
    private bool _isWifiBusy;

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

    /// <summary>扫描无线网络并填充列表(可重复扫描刷新)。</summary>
    [RelayCommand]
    private async Task ScanWifiAsync()
    {
        if (IsWifiBusy)
        {
            return;
        }

        IsWifiBusy = true;
        try
        {
            var networks = await _nmcli.ScanAsync(CancellationToken.None).ConfigureAwait(true);
            WifiSsids.Clear();
            foreach (var network in networks)
            {
                WifiSsids.Add(network.Ssid);
            }

            WifiStatus = WifiSsids.Count == 0 ? L["network.wifi.none"] : string.Empty;
            OnPropertyChanged(nameof(CanConnectWifi));
        }
        catch (Exception ex)
        {
            WifiStatus = $"WiFi: {ex.Message}";
        }
        finally
        {
            IsWifiBusy = false;
        }
    }

    /// <summary>连接选中的无线网络。</summary>
    [RelayCommand]
    private async Task ConnectWifiAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedSsid) || IsWifiBusy)
        {
            return;
        }

        IsWifiBusy = true;
        try
        {
            var (ok, error) = await _nmcli.ConnectAsync(SelectedSsid, WifiPassword, CancellationToken.None).ConfigureAwait(true);
            WifiStatus = ok
                ? string.Format(L["network.wifi.connected"], SelectedSsid)
                : string.Format(L["network.wifi.failed"], error ?? "nmcli");
        }
        finally
        {
            IsWifiBusy = false;
            OnPropertyChanged(nameof(CanConnectWifi));
        }
    }

    public bool CanConnectWifi => !string.IsNullOrWhiteSpace(SelectedSsid) && !IsWifiBusy;

    partial void OnSelectedSsidChanged(string? value) => OnPropertyChanged(nameof(CanConnectWifi));
}
