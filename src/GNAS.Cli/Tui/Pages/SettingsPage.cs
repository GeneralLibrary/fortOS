using Spectre.Console.Rendering;
using GNAS.Cli.Tui;
using GNAS.Cli.ApiClient;
using Spectre.Console;

namespace GNAS.Cli.Tui.Pages;

/// <summary>显示系统设置页面。</summary>
public sealed class SettingsPage : ITuiPage
{
    /// <inheritdoc />
    public string Title => "设置";
    /// <inheritdoc />
    public TimeSpan RefreshInterval => TimeSpan.FromSeconds(15);
    /// <inheritdoc />
    public async Task<IRenderable> RenderAsync(GnasApiClient client, CancellationToken cancellationToken)
    {
        try { using var doc = await client.GetAsync("api/config", cancellationToken); return new Panel(doc.RootElement.ToString()) { Header = new PanelHeader("配置") }; }
        catch (Exception ex) { return PageHelpers.Error(ex); }
    }
    /// <inheritdoc />
    public Task<bool> HandleKeyAsync(ConsoleKeyInfo key, GnasApiClient client, CancellationToken cancellationToken) => Task.FromResult(false);
}
