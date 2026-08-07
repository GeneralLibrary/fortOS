using CommunityToolkit.Mvvm.ComponentModel;
using FortOS.Installer.Gui.Localization;

namespace FortOS.Installer.Gui.ViewModels;

/// <summary>Base class for all page ViewModels.</summary>
public abstract class ViewModelBase : ObservableObject
{
    /// <summary>Page title (wizard header).</summary>
    public virtual string Title => string.Empty;

    /// <summary>UI text resource (Chinese/English), referenced from XAML with <c>{Binding L[key]}</c>.</summary>
    public LocalizationService L => LocalizationService.Current;

    /// <summary>IsValid change notification (the wizard uses this to refresh the "Next" enabled state).</summary>
    public event EventHandler? IsValidChanged;

    /// <summary>Called by derived classes on input changes to notify the wizard to refresh navigation.</summary>
    protected void RaiseIsValidChanged() => IsValidChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>Wizard page contract: validation state and change notification.</summary>
public interface IWizardPage
{
    string Title { get; }

    /// <summary>Whether the current input is sufficient to advance to the next page.</summary>
    bool IsValid { get; }

    /// <summary>Raised when IsValid changes (drives the "Next" enabled state).</summary>
    event EventHandler? IsValidChanged;
}
