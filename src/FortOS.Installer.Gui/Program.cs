using Avalonia;
using Avalonia.Logging;

namespace FortOS.Installer.Gui;

internal static class Program
{
    // Initialization code. Do not use any Avalonia, third-party API, or dependency injection related code
    // in any location whose signature is not AppMain.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration. Do not delete or modify — this entry point is shared by headless tests and live environment startup.
    // Theme: Semi.Avalonia 12 + Ursa.Avalonia 2 (Semi style), all referenced in App.axaml's
    // Application.Styles via SemiTheme / UrsaSemiTheme (neither library at 12.x has an
    // AppBuilder extension; pure XAML usage is the official approach).
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace(LogEventLevel.Warning);
}
