using System.ComponentModel;

namespace FortOS.Installer.Gui.Localization;

/// <summary>
/// 安装器界面文案的中/英资源服务(设计稿 §1.1 多语言缺失的落点)。
/// ViewModel 暴露 <c>L</c> 后,XAML 以 <c>{Binding L[key]}</c> 引用;
/// 语言切换触发索引器变更通知,全部已绑定文案即时更新。
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    public static LocalizationService Current { get; } = new();

    private string _language = "en";

    private LocalizationService()
    {
    }

    /// <summary>当前 UI 语言:<c>en</c> / <c>zh</c>。</summary>
    public string Language => _language;

    /// <summary>按 key 取文案;缺失时回退英文,再缺失时返回 key 本身。</summary>
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

    /// <summary>切换 UI 语言并通知所有索引器绑定。</summary>
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

    /// <summary>供 VM 的 get-only 资源属性调用(触发 PropertyChanged 通道)。</summary>
    public void NotifyAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));

    // ------------------------------------------------------------------
    // 文案表。key 即英文原文,便于缺失时回退。
    // ------------------------------------------------------------------
    private static readonly Dictionary<string, Dictionary<string, string>> Strings = new()
    {
        ["en"] = new Dictionary<string, string>
        {
            // 通用 / 导航
            ["window.title"] = "FortOS Installer",
            ["nav.next"] = "Next ›",
            ["nav.back"] = "‹ Back",
            ["wizard.step"] = "Step {0} of {1}",

            // 欢迎
            ["welcome.title"] = "FortOS",
            ["welcome.subtitle"] = "NAS operating system installer",
            ["welcome.language"] = "Language",
            ["welcome.keyboard"] = "Keyboard layout",

            // 磁盘布局
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

            // 网络
            ["network.hostname"] = "Hostname",
            ["network.mode"] = "Mode",
            ["network.address"] = "IP address (CIDR, e.g. 192.168.1.10/24)",
            ["network.gateway"] = "Gateway",
            ["network.dns"] = "DNS servers (comma separated)",
            ["network.hint"] = "DHCP is used when static address is left empty.",

            // 账户
            ["account.username"] = "Admin username",
            ["account.timezone"] = "Timezone",
            ["account.password"] = "Password",
            ["account.confirmPassword"] = "Confirm password",
            ["account.strength"] = "Password strength: {0}",
            ["account.sshKey"] = "Optional SSH public key",
            ["strength.weak"] = "Weak",
            ["strength.ok"] = "OK",
            ["strength.strong"] = "Strong",

            // 确认
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

            // 执行
            ["install.phase"] = "Phase: {0}",
            ["install.retry"] = "Retry",
            ["install.reboot"] = "Reboot",

            // 完成
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
