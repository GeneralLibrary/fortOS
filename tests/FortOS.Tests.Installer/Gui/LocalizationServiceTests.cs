using FortOS.Installer.Core.Logging;
using FortOS.Installer.Core.Models;
using FortOS.Installer.Core.Session;
using FortOS.Installer.Core.Steps;
using FortOS.Installer.Core.Tools;
using FortOS.Installer.Gui.Localization;
using FortOS.Installer.Gui.ViewModels;
using FortOS.Tests.Installer.Fakes;

namespace FortOS.Tests.Installer.Gui;

public class LocalizationServiceTests
{
    private static void Reset() => LocalizationService.Current.SetLanguage("en");

    [Fact]
    public void DefaultsToEnglish_FallsBackForMissingKey()
    {
        Reset();
        Assert.Equal("en", LocalizationService.Current.Language);
        Assert.Equal("NAS operating system installer", LocalizationService.Current["welcome.subtitle"]);
        Assert.Equal("missing.key", LocalizationService.Current["missing.key"]);
    }

    [Fact]
    public void SetLanguageZh_SwitchesTexts()
    {
        Reset();
        LocalizationService.Current.SetLanguage("zh");
        Assert.Equal("zh", LocalizationService.Current.Language);
        Assert.Equal("NAS 操作系统安装向导", LocalizationService.Current["welcome.subtitle"]);
        Assert.Equal("下一步 ›", LocalizationService.Current["nav.next"]);
        Reset();
    }

    [Fact]
    public void SetLanguage_NormalizesAndIdempotent()
    {
        Reset();
        LocalizationService.Current.SetLanguage("zh_CN.UTF-8"); // 归一化
        Assert.Equal("zh", LocalizationService.Current.Language);
        LocalizationService.Current.SetLanguage("zh"); // 幂等
        Assert.Equal("zh", LocalizationService.Current.Language);
        Reset();
    }

    [Fact]
    public void Welcome_LanguageSelection_SwitchesUiLanguage()
    {
        Reset();
        var welcome = new WelcomeViewModel();
        welcome.Language = "zh_CN.UTF-8";
        Assert.Equal("zh", LocalizationService.Current.Language);
        Assert.Equal("NAS 操作系统安装向导", welcome.L["welcome.subtitle"]);
        welcome.Language = "en_US.UTF-8";
        Assert.Equal("en", LocalizationService.Current.Language);
    }

    [Fact]
    public void WizardStepIndicator_IsLocalized()
    {
        Reset();
        var wizard = new WizardViewModel([new WelcomeViewModel()]);
        Assert.Equal("Step 1 of 1", wizard.StepIndicator);
        LocalizationService.Current.SetLanguage("zh");
        Assert.Equal("第 1 步,共 1 步", wizard.StepIndicator);
        Reset();
    }

    [Fact]
    public void AccountPasswordStrength_IsLocalized()
    {
        Reset();
        var vm = new AccountViewModel { Username = "admin", Password = "x", ConfirmPassword = "x" };
        Assert.Equal("Password strength: Weak", vm.PasswordStrength);
        LocalizationService.Current.SetLanguage("zh");
        Assert.Equal("密码强度:弱", vm.PasswordStrength);
        Reset();
    }

    [Fact]
    public void InstallPhaseText_IsLocalized()
    {
        Reset();
        var session = new InstallerSession([], new LsblkTool(new FakeRunner()), new RingLog());
        var vm = new InstallViewModel(() => session, action => action());
        vm.Phase = InstallerPhase.Copying;
        Assert.Equal("Phase: Copying", vm.PhaseText);
        LocalizationService.Current.SetLanguage("zh");
        Assert.Equal("阶段:Copying", vm.PhaseText);
        Reset();
    }

    [Fact]
    public void CompleteGuidance_IsLocalized()
    {
        Reset();
        var vm = new CompleteViewModel();
        Assert.Contains("first-boot wizard", vm.Guidance);
        LocalizationService.Current.SetLanguage("zh");
        Assert.Contains("首次启动向导", vm.Guidance);
        Reset();
    }
}
