using System.Diagnostics;

namespace FortOS.Installer.Gui.ViewModels;

/// <summary>
/// System control after installation completes/fails (reboot / shutdown). Shared by multiple GUI pages,
/// avoiding duplicate systemctl call implementations.
/// </summary>
public static class SystemControl
{
    public static void Reboot()
        => Process.Start(new ProcessStartInfo("systemctl", "reboot") { UseShellExecute = false });

    public static void PowerOff()
        => Process.Start(new ProcessStartInfo("systemctl", "poweroff") { UseShellExecute = false });
}
