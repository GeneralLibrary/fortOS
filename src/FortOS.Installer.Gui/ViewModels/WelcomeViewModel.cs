using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using FortOS.Installer.Gui.Localization;

namespace FortOS.Installer.Gui.ViewModels;

/// <summary>Page 1: welcome / language / keyboard.</summary>
public partial class WelcomeViewModel : ViewModelBase, IWizardPage
{
    public override string Title => "Welcome";

    /// <summary>Installer version (branding display).</summary>
    public string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public IReadOnlyList<string> Languages { get; } = ["en_US.UTF-8", "zh_CN.UTF-8", "de_DE.UTF-8", "ja_JP.UTF-8"];

    public IReadOnlyList<string> Keyboards { get; } = ["us", "de", "fr", "gb", "jp"];

    [ObservableProperty]
    private string _language = "en_US.UTF-8";

    [ObservableProperty]
    private string _keyboard = "us";

    /// <summary>Language selection is linked to the UI language (en_US.UTF-8 → en, zh_CN.UTF-8 → zh).</summary>
    partial void OnLanguageChanged(string value) => LocalizationService.Current.SetLanguage(value);

    public bool IsValid => true;
}
