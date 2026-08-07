using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FortOS.Installer.Gui.ViewModels;

/// <summary>Completion page: shows access guidance, reboot / shutdown.</summary>
public partial class CompleteViewModel : ViewModelBase, IWizardPage
{
    public override string Title => "Done";

    /// <summary>First-boot guidance (localized, design spec 4): after reboot the FortOS API enters first-boot.</summary>
    public string Guidance => L["complete.guidance"];

    public bool IsValid => false;

    [RelayCommand]
    private static void Reboot() => SystemControl.Reboot();

    [RelayCommand]
    private static void Shutdown() => SystemControl.PowerOff();
}
