using FortOS.Installer.Gui.ViewModels;

namespace FortOS.Tests.Installer.Gui;

[Collection("Gui.Localization")]
public class WizardViewModelTests
{
    private sealed class PageStub(string title, bool isValid, bool mutable = false) : IWizardPage
    {
        public string Title => title;
        public bool IsValid { get; private set; } = isValid;
        public event EventHandler? IsValidChanged;

        public void SetValid(bool value)
        {
            IsValid = value;
            IsValidChanged?.Invoke(this, EventArgs.Empty);
        }

        public bool Mutable => mutable;
    }

    [Fact]
    public void StartsOnFirstPage_CanGoNextWhenValid()
    {
        var wizard = new WizardViewModel([new PageStub("A", true), new PageStub("B", true)]);

        Assert.Equal(0, wizard.CurrentIndex);
        Assert.True(wizard.CanGoNext);
        Assert.False(wizard.CanGoBack);
        Assert.True(wizard.IsWizardNavigationVisible);
    }

    [Fact]
    public void Next_AdvancesAndBack_Returns()
    {
        var wizard = new WizardViewModel([new PageStub("A", true), new PageStub("B", true), new PageStub("C", true)]);

        wizard.NextCommand.Execute(null);
        Assert.Equal(1, wizard.CurrentIndex);
        wizard.NextCommand.Execute(null);
        Assert.Equal(2, wizard.CurrentIndex);
        Assert.False(wizard.CanGoNext); // 最后一页

        wizard.BackCommand.Execute(null);
        Assert.Equal(1, wizard.CurrentIndex);
    }

    [Fact]
    public void CanGoNext_ReflectsCurrentPageValidity()
    {
        var pageA = new PageStub("A", isValid: false);
        var wizard = new WizardViewModel([pageA, new PageStub("B", true)]);

        Assert.False(wizard.CanGoNext);
        pageA.SetValid(true);
        Assert.True(wizard.CanGoNext);
    }

    [Fact]
    public void JumpTo_ForcesPageChangeWithoutValidity()
    {
        var wizard = new WizardViewModel([new PageStub("A", true), new PageStub("B", true), new PageStub("C", true)]);

        wizard.JumpTo(2);
        Assert.Equal(2, wizard.CurrentIndex);
    }

    [Fact]
    public void NavigationHiddenOnInstallAndCompletePages()
    {
        // 通过 MainWindowViewModel 组装,验证执行/完成页隐藏导航。
        var vm = new MainWindowViewModel(lsblkFactory: () => new FortOS.Installer.Core.Tools.LsblkTool(new Fakes.FakeRunner()));
        Assert.True(vm.Wizard.IsWizardNavigationVisible);

        vm.Wizard.JumpTo(5); // Install
        Assert.False(vm.Wizard.IsWizardNavigationVisible);

        vm.Wizard.JumpTo(6); // Complete
        Assert.False(vm.Wizard.IsWizardNavigationVisible);
    }
}
