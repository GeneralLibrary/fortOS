using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using FortOS.Installer.Core.Models;

namespace FortOS.Installer.Gui.ViewModels;

/// <summary>第 5 页:安装计划确认(开始安装后进入不可回退区)。</summary>
public partial class ConfirmViewModel : ViewModelBase, IWizardPage
{
    private readonly WelcomeViewModel _welcome;
    private readonly DiskLayoutViewModel _disk;
    private readonly NetworkViewModel _network;
    private readonly AccountViewModel _account;

    public ConfirmViewModel(
        WelcomeViewModel welcome,
        DiskLayoutViewModel disk,
        NetworkViewModel network,
        AccountViewModel account)
    {
        _welcome = welcome;
        _disk = disk;
        _network = network;
        _account = account;
        foreach (var page in new IWizardPage[] { welcome, disk, network, account })
        {
            page.IsValidChanged += (_, _) => OnPropertyChanged(nameof(Summary));
        }
    }

    public override string Title => "Confirm";

    public string Summary => BuildSummary();

    public bool IsValid => true;

    private string BuildSummary()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{L["confirm.summary.systemDisk"]}{_disk.SelectedSystemDisk?.Path} — {_disk.RootFs}");
        sb.AppendLine($"{L["confirm.summary.swap"]}{DescribeSwap()}");
        sb.AppendLine($"{L["confirm.summary.data"]}{DescribeData()}");
        sb.AppendLine($"{L["confirm.summary.network"]}{DescribeNetwork()}");
        sb.AppendLine($"{L["confirm.summary.hostname"]}{_network.Hostname}");
        sb.AppendLine($"{L["confirm.summary.admin"]}{_account.Username} ({_account.Timezone})");
        sb.AppendLine($"{L["confirm.summary.locale"]}{_welcome.Language} / keyboard {_welcome.Keyboard}");
        return sb.ToString();
    }

    private string DescribeSwap() => _disk.SwapMode switch
    {
        SwapMode.Off => "off",
        SwapMode.Fixed => $"{_disk.SwapSizeMiB} MiB",
        _ => "auto (RAM size)",
    };

    private string DescribeData() => _disk.DataMode switch
    {
        DataDiskMode.Single => $"{_disk.SelectedDataDisk?.Path} — {_disk.DataFs} (label {_disk.DataLabel})",
        _ => "not configured (post-install)",
    };

    private string DescribeNetwork() => _network.Mode switch
    {
        NetworkMode.Static => $"static {_network.Address}{(string.IsNullOrEmpty(_network.Gateway) ? "" : $" gw {_network.Gateway}")}",
        _ => "DHCP",
    };
}
