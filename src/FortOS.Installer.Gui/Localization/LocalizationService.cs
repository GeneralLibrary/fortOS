using System.ComponentModel;

namespace FortOS.Installer.Gui.Localization;

/// <summary>
/// Chinese/English resource service for installer UI text (the implementation point for the missing
/// localization in design spec §1.1).
/// Once the ViewModel exposes <c>L</c>, XAML references it via <c>{Binding L[key]}</c>;
/// language switching triggers the indexer change notification, and all bound texts update immediately.
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    public static LocalizationService Current { get; } = new();

    private string _language = "en";

    private LocalizationService()
    {
    }

    /// <summary>Current UI language: <c>en</c> / <c>zh</c>.</summary>
    public string Language => _language;

    /// <summary>Gets text by key; falls back to English when missing, and returns the key itself if still missing.</summary>
    public string this[string key] => Get(key);

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Get(string key)
    {
        if (Strings.TryGetValue(_language, out var table) && table.TryGetValue(key, out var value))
        {
            return value;
        }
        return Strings["en"].TryGetValue(key, out var fallback) ? fallback : key;
    }

    /// <summary>Switches the UI language and notifies all indexer bindings.</summary>
    public void SetLanguage(string language)
    {
        var normalized = language.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh" : "en";
        if (_language == normalized)
        {
            return;
        }
        _language = normalized;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
    }

    /// <summary>Called by get-only resource properties on VMs (triggers the PropertyChanged channel).</summary>
    public void NotifyAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));

    // ------------------------------------------------------------------
    // String table. The key is the English source text, so missing keys can fall back to English.
    // ------------------------------------------------------------------
    private static readonly Dictionary<string, Dictionary<string, string>> Strings = new()
    {
        ["en"] = new Dictionary<string, string>
        {
            // General / navigation
            ["window.title"] = "FortOS Installer",
            ["nav.next"] = "Next ›",
            ["nav.back"] = "‹ Back",
            ["wizard.step"] = "Step {0} of {1}",
            ["status.noNetwork"] = "No network",
            ["status.managementTip"] = "FortOS management address — open in a web browser on another machine.",

            // Welcome
            ["welcome.title"] = "FortOS",
            ["welcome.subtitle"] = "NAS operating system installer",
            ["welcome.language"] = "Language",
            ["welcome.keyboard"] = "Keyboard layout",

            // Disk layout
            ["disk.warning"] = "Choose the system disk. Everything on it will be ERASED.",
            ["disk.systemDisk"] = "System disk",
            ["disk.rootFs"] = "Root filesystem",
            ["disk.swap"] = "Swap",
            ["disk.swapSize"] = "Swap size (MiB)",
            ["disk.dataDisk"] = "Data disk",
            ["disk.dataDiskHint"] = "Data disk (formatting will erase it)",
            ["disk.fs"] = "Filesystem",
            ["disk.label"] = "Label",
            ["disk.noDisks"] = "No writable disks found.",

            // Network
            ["network.hostname"] = "Hostname",
            ["network.mode"] = "Mode",
            ["network.address"] = "IP address (CIDR, e.g. 192.168.1.10/24)",
            ["network.gateway"] = "Gateway",
            ["network.dns"] = "DNS servers (comma separated)",
            ["network.hint"] = "DHCP is used when static address is left empty.",
            ["network.wifi.title"] = "WiFi",
            ["network.wifi.scan"] = "Scan networks",
            ["network.wifi.ssid"] = "Select a wireless network",
            ["network.wifi.password"] = "WiFi password",
            ["network.wifi.connect"] = "Connect",
            ["network.wifi.connected"] = "Connected to {0}.",
            ["network.wifi.failed"] = "Connection failed: {0}",
            ["network.wifi.none"] = "No wireless networks found. Scan again or use a wired connection.",

            // Account
            ["account.username"] = "Admin username",
            ["account.timezone"] = "Timezone",
            ["account.password"] = "Password",
            ["account.confirmPassword"] = "Confirm password",
            ["account.strength"] = "Password strength: {0}",
            ["account.sshKey"] = "Optional SSH public key",
            ["strength.weak"] = "Weak",
            ["strength.ok"] = "OK",
            ["strength.strong"] = "Strong",

            // Confirmation
            ["confirm.title"] = "Review your installation plan",
            ["confirm.warning"] = "After this point you cannot go back.",
            ["confirm.begin"] = "Begin installation",
            ["confirm.summary.systemDisk"] = "System disk:  ",
            ["confirm.summary.swap"] = "Swap:         ",
            ["confirm.summary.data"] = "Data disk:    ",
            ["confirm.summary.network"] = "Network:      ",
            ["confirm.summary.hostname"] = "Hostname:     ",
            ["confirm.summary.admin"] = "Admin user:   ",
            ["confirm.summary.locale"] = "Language:     ",

            // Execution
            ["install.phase"] = "Phase: {0}",
            ["install.retry"] = "Retry",
            ["install.reboot"] = "Reboot",

            // Completion
            ["complete.title"] = "✓ Installation complete",
            ["complete.guidance"] = "Installation complete. Reboot and remove the installation media.\nOn first boot, FortOS runs the first-boot wizard to create the admin token and mount the data disk.",
            ["complete.reboot"] = "Reboot now",
            ["complete.shutdown"] = "Shut down",
        },
        ["zh"] = new Dictionary<string, string>
        {
            ["window.title"] = "FortOS 安装器",
            ["nav.next"] = "下一步 ›",
            ["nav.back"] = "‹ 上一步",
            ["wizard.step"] = "第 {0} 步,共 {1} 步",
            ["status.noNetwork"] = "未检测到网络",
            ["status.managementTip"] = "FortOS 管理入口 — 在另一台机器的浏览器中打开。",

            ["welcome.title"] = "FortOS",
            ["welcome.subtitle"] = "NAS 操作系统安装向导",
            ["welcome.language"] = "语言",
            ["welcome.keyboard"] = "键盘布局",

            ["disk.warning"] = "请选择系统盘。该盘上的所有数据将被清除。",
            ["disk.systemDisk"] = "系统盘",
            ["disk.rootFs"] = "根文件系统",
            ["disk.swap"] = "交换分区",
            ["disk.swapSize"] = "交换分区大小(MiB)",
            ["disk.dataDisk"] = "数据盘",
            ["disk.dataDiskHint"] = "数据盘(格式化将清除数据)",
            ["disk.fs"] = "文件系统",
            ["disk.label"] = "卷标",
            ["disk.noDisks"] = "未发现可写磁盘。",

            ["network.hostname"] = "主机名",
            ["network.mode"] = "模式",
            ["network.address"] = "IP 地址(CIDR,如 192.168.1.10/24)",
            ["network.gateway"] = "网关",
            ["network.dns"] = "DNS 服务器(逗号分隔)",
            ["network.hint"] = "留空静态地址时使用 DHCP。",
            ["network.wifi.title"] = "无线网络",
            ["network.wifi.scan"] = "扫描网络",
            ["network.wifi.ssid"] = "选择无线网络",
            ["network.wifi.password"] = "WiFi 密码",
            ["network.wifi.connect"] = "连接",
            ["network.wifi.connected"] = "已连接到 {0}。",
            ["network.wifi.failed"] = "连接失败:{0}",
            ["network.wifi.none"] = "未发现无线网络。请重新扫描或使用有线连接。",

            ["account.username"] = "管理员用户名",
            ["account.timezone"] = "时区",
            ["account.password"] = "密码",
            ["account.confirmPassword"] = "确认密码",
            ["account.strength"] = "密码强度:{0}",
            ["account.sshKey"] = "可选 SSH 公钥",
            ["strength.weak"] = "弱",
            ["strength.ok"] = "中",
            ["strength.strong"] = "强",

            ["confirm.title"] = "确认安装计划",
            ["confirm.warning"] = "此步骤之后将无法返回。",
            ["confirm.begin"] = "开始安装",
            ["confirm.summary.systemDisk"] = "系统盘:    ",
            ["confirm.summary.swap"] = "交换分区:  ",
            ["confirm.summary.data"] = "数据盘:    ",
            ["confirm.summary.network"] = "网络:      ",
            ["confirm.summary.hostname"] = "主机名:    ",
            ["confirm.summary.admin"] = "管理员:    ",
            ["confirm.summary.locale"] = "语言:      ",

            ["install.phase"] = "阶段:{0}",
            ["install.retry"] = "重试",
            ["install.reboot"] = "重启",

            ["complete.title"] = "✓ 安装完成",
            ["complete.guidance"] = "安装完成。请重启并移除安装介质。\n首次启动时,FortOS 将运行首次启动向导以创建管理员令牌并挂载数据盘。",
            ["complete.reboot"] = "立即重启",
            ["complete.shutdown"] = "关机",
        },
    };
}
