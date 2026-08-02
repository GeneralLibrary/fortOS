using Avalonia;
using Avalonia.Logging;

namespace FortOS.Installer.Gui;

internal static class Program
{
    // 初始化代码。请不要使用任何 Avalonia、第三方 API 或依赖注入相关代码
    // 到不以 AppMain 为签名的任何位置。
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia 配置。请勿删除,也不要修改 —— 无头测试与 live 环境启动共用此入口。
    // 主题:Semi.Avalonia 12 + Ursa.Avalonia 2(Semi 风格)全部在 App.axaml 的
    // Application.Styles 中通过 SemiTheme / UrsaSemiTheme 引入(两库 12.x 均无
    // AppBuilder 扩展,纯 XAML 方式即官方用法)。
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace(LogEventLevel.Warning);
}
