using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FortOS.Installer.Gui.Localization;

namespace FortOS.Installer.Gui.ViewModels;

/// <summary>
/// Wizard navigation (design spec 4): sequential navigation with back support; navigation is hidden once the execution page is reached.
/// </summary>
public partial class WizardViewModel : ViewModelBase
{
    private readonly IReadOnlyList<IWizardPage> _pages;

    public WizardViewModel(IEnumerable<IWizardPage> pages)
    {
        _pages = [.. pages];
        _currentPage = _pages[0];
        foreach (var page in _pages)
        {
            page.IsValidChanged += (_, _) => NotifyCanExecuteChanged();
        }
        // Refresh the step indicator on language switch.
        L.PropertyChanged += (_, _) => OnPropertyChanged(nameof(StepIndicator));
    }

    [ObservableProperty]
    private IWizardPage _currentPage;

    [ObservableProperty]
    private int _currentIndex;

    public int PageCount => _pages.Count;

    /// <summary>Header step indicator, e.g. <c>Step 3 of 7</c> (localized).</summary>
    public string StepIndicator => string.Format(L["wizard.step"], CurrentIndex + 1, PageCount);

    /// <summary>Hides the navigation buttons on the execution/completion pages.</summary>
    public bool IsWizardNavigationVisible => CurrentPage is not InstallViewModel and not CompleteViewModel;

    public bool CanGoNext => CurrentIndex < PageCount - 1 && CurrentPage.IsValid;

    public bool CanGoBack => CurrentIndex > 0 && IsWizardNavigationVisible;

    /// <summary>Internal program navigation (auto-advance to the completion page when installation finishes); does not validate IsValid.</summary>
    public void JumpTo(int index)
    {
        if (index < 0 || index >= PageCount)
        {
            return;
        }
        CurrentIndex = index;
        CurrentPage = _pages[index];
        NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next()
    {
        if (CurrentIndex >= PageCount - 1)
        {
            return;
        }
        CurrentIndex++;
        CurrentPage = _pages[CurrentIndex];
        NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        if (CurrentIndex <= 0)
        {
            return;
        }
        CurrentIndex--;
        CurrentPage = _pages[CurrentIndex];
        NotifyCanExecuteChanged();
    }

    /// <summary>Refreshes commands when the current page's IsValid or navigation properties change.</summary>
    private void NotifyCanExecuteChanged()
    {
        OnPropertyChanged(nameof(IsWizardNavigationVisible));
        NextCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
    }

    partial void OnCurrentPageChanged(IWizardPage value)
    {
        OnPropertyChanged(nameof(StepIndicator));
        NotifyCanExecuteChanged();
    }

    partial void OnCurrentIndexChanged(int value)
    {
        OnPropertyChanged(nameof(StepIndicator));
        NotifyCanExecuteChanged();
    }
}
