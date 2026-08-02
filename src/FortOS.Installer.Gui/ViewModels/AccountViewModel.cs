using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FortOS.Installer.Gui.ViewModels;

/// <summary>第 4 页:管理员账户 / 密码强度 / SSH 公钥 / 时区。</summary>
public partial class AccountViewModel : ViewModelBase, IWizardPage
{
    [GeneratedRegex("^[a-z_][a-z0-9_-]{0,31}$")]
    private static partial Regex UsernameRegex();

    public override string Title => "Account";

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _confirmPassword = string.Empty;

    [ObservableProperty]
    private string _sshKey = string.Empty;

    [ObservableProperty]
    private string _timezone = "Asia/Shanghai";

    public IReadOnlyList<string> Timezones { get; } =
    [
        "UTC", "Asia/Shanghai", "Asia/Tokyo", "Asia/Singapore",
        "Europe/Berlin", "Europe/London", "America/New_York", "America/Los_Angeles",
    ];

    public bool IsValid =>
        UsernameRegex().IsMatch(Username) &&
        Password.Length >= 6 &&
        Password == ConfirmPassword &&
        !string.IsNullOrWhiteSpace(Timezone);

    /// <summary>密码强度提示(本地化):Weak / OK / Strong。</summary>
    public string PasswordStrength
    {
        get
        {
            var strength = Password.Length switch
            {
                < 6 => "strength.weak",
                < 10 => "strength.ok",
                _ => "strength.strong",
            };
            return string.Format(L["account.strength"], L[strength]);
        }
    }

    partial void OnUsernameChanged(string value) => RaiseIsValidChanged();
    partial void OnPasswordChanged(string value)
    {
        OnPropertyChanged(nameof(PasswordStrength));
        RaiseIsValidChanged();
    }

    partial void OnConfirmPasswordChanged(string value) => RaiseIsValidChanged();

    partial void OnTimezoneChanged(string value) => RaiseIsValidChanged();
}
