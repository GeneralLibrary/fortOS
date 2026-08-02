using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FortOS.Installer.Gui.Localization;

namespace FortOS.Installer.Gui.ViewModels;

/// <summary>
/// 向导导航(设计稿 4):七页顺序导航,支持回退;进入执行页后隐藏导航。
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
        // 语言切换时刷新步骤指示。
        L.PropertyChanged += (_, _) => OnPropertyChanged(nameof(StepIndicator));
    }

    [ObservableProperty]
    private IWizardPage _currentPage;

    [ObservableProperty]
    private int _currentIndex;

    public int PageCount => _pages.Count;

    /// <summary>页眉步骤指示,如 <c>Step 3 of 7</c>(本地化)。</summary>
    public string StepIndicator => string.Format(L["wizard.step"], CurrentIndex + 1, PageCount);

    /// <summary>执行/完成页隐藏导航按钮。</summary>
    public bool IsWizardNavigationVisible => CurrentPage is not InstallViewModel and not CompleteViewModel;

    public bool CanGoNext => CurrentIndex < PageCount - 1 && CurrentPage.IsValid;

    public bool CanGoBack => CurrentIndex > 0 && IsWizardNavigationVisible;

    /// <summary>程序内部跳转(安装完成自动前进到完成页),不校验 IsValid。</summary>
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

    /// <summary>当前页 IsValid 或导航属性变化时刷新命令。</summary>
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
