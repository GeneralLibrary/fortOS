using Spectre.Console.Rendering;
using GNAS.Cli.ApiClient;
using GNAS.Cli.Tui.Pages;
using Spectre.Console;

namespace GNAS.Cli.Tui;

/// <summary>负责 TUI Live 渲染与页面切换。</summary>
public sealed class TuiRenderer
{
    private readonly List<ITuiPage> _pages;
    private int _index;

    /// <summary>建立默认 TUI 渲染器。</summary>
    public TuiRenderer()
    {
        _pages = [new DashboardPage(), new DiskPage(), new ServicePage(), new AgentPage(), new LogPage(), new AlertPage(), new SettingsPage()];
    }

    /// <summary>启动互动界面。</summary>
    public async Task<int> RunAsync(GnasApiClient client, CancellationToken cancellationToken = default)
    {
        try
        {
            await AnsiConsole.Live(new Panel("正在载入 GNAS...")).AutoClear(false).StartAsync(async ctx =>
            {
                var quit = false;
                while (!quit && !cancellationToken.IsCancellationRequested)
                {
                    while (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true);
                        if (key.Key is ConsoleKey.Q) { quit = true; break; }
                        if (key.Key is ConsoleKey.Escape) { _index = 0; continue; }
                        var switched = SwitchPage(key);
                        if (!switched) await _pages[_index].HandleKeyAsync(key, client, cancellationToken);
                    }

                    var body = await SafeRenderAsync(client, cancellationToken);
                    ctx.UpdateTarget(Wrap(body));
                    await Task.Delay(_pages[_index].RefreshInterval, cancellationToken);
                }
            });
            return 0;
        }
        catch (OperationCanceledException) { return 0; }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"TUI 已退出：{ex.Message}");
            return 1;
        }
    }

    private bool SwitchPage(ConsoleKeyInfo key)
    {
        var next = key.Key switch
        {
            ConsoleKey.F1 or ConsoleKey.D1 or ConsoleKey.NumPad1 => 0,
            ConsoleKey.F2 or ConsoleKey.D2 or ConsoleKey.NumPad2 => 1,
            ConsoleKey.F3 or ConsoleKey.D3 or ConsoleKey.NumPad3 => 2,
            ConsoleKey.F4 or ConsoleKey.D4 or ConsoleKey.NumPad4 => 3,
            ConsoleKey.F5 or ConsoleKey.D5 or ConsoleKey.NumPad5 => 4,
            ConsoleKey.F6 or ConsoleKey.D6 or ConsoleKey.NumPad6 => 5,
            ConsoleKey.F7 or ConsoleKey.D7 or ConsoleKey.NumPad7 => 6,
            _ => -1
        };
        if (next < 0) return false;
        _index = next;
        return true;
    }

    private async Task<IRenderable> SafeRenderAsync(GnasApiClient client, CancellationToken cancellationToken)
    {
        try { return await _pages[_index].RenderAsync(client, cancellationToken); }
        catch (GnasApiException ex) { return new Panel(Markup.Escape(ex.Message)) { Header = new PanelHeader("无法连接") }; }
        catch (Exception ex) { return new Panel(Markup.Escape("页面渲染失败：" + ex.Message)); }
    }

    private IRenderable Wrap(IRenderable body)
    {
        var menu = "[bold]F1/1[/]总览 [bold]F2/2[/]磁盘 [bold]F3/3[/]服务 [bold]F4/4[/]代理 [bold]F5/5[/]日志 [bold]F6/6[/]警报 [bold]F7/7[/]设置 [bold]Esc[/]总览 [bold]q[/]退出";
        return new Rows(new Markup(menu), new Rule(_pages[_index].Title), body);
    }
}
