using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FortOS.Installer.Gui.ViewModels;

/// <summary>完成页:展示访问指引,重启/关机。</summary>
public partial class CompleteViewModel : ViewModelBase, IWizardPage
{
    public override string Title => "Done";

    /// <summary>首次启动指引(本地化,设计稿 4):重启后 FortOS API 进入 first-boot。</summary>
    public string Guidance => L["complete.guidance"];

    public bool IsValid => false;

    [RelayCommand]
    private static void Reboot() => SystemControl.Reboot();

    [RelayCommand]
    private static void Shutdown() => SystemControl.PowerOff();
}
