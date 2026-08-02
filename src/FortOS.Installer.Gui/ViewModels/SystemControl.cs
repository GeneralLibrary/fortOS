using System.Diagnostics;

namespace FortOS.Installer.Gui.ViewModels;

/// <summary>
/// 安装完成/失败后的系统控制(重启/关机)。GUI 多个页面共用,
/// 避免 systemctl 调用重复实现。
/// </summary>
public static class SystemControl
{
    public static void Reboot()
        => Process.Start(new ProcessStartInfo("systemctl", "reboot") { UseShellExecute = false });

    public static void PowerOff()
        => Process.Start(new ProcessStartInfo("systemctl", "poweroff") { UseShellExecute = false });
}
