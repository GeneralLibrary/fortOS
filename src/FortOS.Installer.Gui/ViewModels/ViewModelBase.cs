using CommunityToolkit.Mvvm.ComponentModel;
using FortOS.Installer.Gui.Localization;

namespace FortOS.Installer.Gui.ViewModels;

/// <summary>所有页面 ViewModel 基类。</summary>
public abstract class ViewModelBase : ObservableObject
{
    /// <summary>页面标题(向导页眉)。</summary>
    public virtual string Title => string.Empty;

    /// <summary>界面文案资源(中/英),XAML 以 <c>{Binding L[key]}</c> 引用。</summary>
    public LocalizationService L => LocalizationService.Current;

    /// <summary>IsValid 变化通知(向导据此刷新「下一步」可执行状态)。</summary>
    public event EventHandler? IsValidChanged;

    /// <summary>派生类在输入变化时调用,通知向导刷新导航。</summary>
    protected void RaiseIsValidChanged() => IsValidChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>向导页契约:校验状态与变更通知。</summary>
public interface IWizardPage
{
    string Title { get; }

    /// <summary>当前输入是否足以进入下一页。</summary>
    bool IsValid { get; }

    /// <summary>IsValid 变化时触发(驱动「下一步」可执行状态)。</summary>
    event EventHandler? IsValidChanged;
}
