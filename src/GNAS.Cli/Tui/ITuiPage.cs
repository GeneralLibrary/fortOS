using Spectre.Console.Rendering;
using GNAS.Cli.ApiClient;
using Spectre.Console;

namespace GNAS.Cli.Tui;

/// <summary>定义 TUI 页面契约。</summary>
public interface ITuiPage
{
    /// <summary>页面标题。</summary>
    string Title { get; }
    /// <summary>刷新间隔。</summary>
    TimeSpan RefreshInterval { get; }
    /// <summary>渲染页面。</summary>
    Task<IRenderable> RenderAsync(GnasApiClient client, CancellationToken cancellationToken);
    /// <summary>处理按键；返回 true 表示已处理。</summary>
    Task<bool> HandleKeyAsync(ConsoleKeyInfo key, GnasApiClient client, CancellationToken cancellationToken);
}
