using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FortOS.Installer.Core.Models;
using FortOS.Installer.Core.Tools;

namespace FortOS.Installer.Gui.ViewModels;

/// <summary>
/// Page 3: network configuration. DHCP is the default; a static address is optional;
/// wireless networks are scanned and connected via nmcli (NetworkManager); after a successful connection the address is obtained via DHCP.
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

    /// <summary>Shows the IP/gateway/DNS inputs in static mode.</summary>
    public bool ShowStaticFields => Mode == NetworkMode.Static;

    partial void OnModeChanged(NetworkMode value)
    {
        OnPropertyChanged(nameof(ShowStaticFields));
        RaiseIsValidChanged();
    }

    partial void OnHostnameChanged(string value) => RaiseIsValidChanged();

    partial void OnAddressChanged(string value) => RaiseIsValidChanged();

    /// <summary>Scans wireless networks and fills the list (can be re-scanned to refresh).</summary>
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

    /// <summary>Connects to the selected wireless network.</summary>
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
