using Spectre.Console.Rendering;
using GNAS.Cli.Tui;
using GNAS.Cli.ApiClient;
using Spectre.Console;

namespace GNAS.Cli.Tui.Pages;

/// <summary>显示系统总览页面。</summary>
public sealed class DashboardPage : ITuiPage
{
    /// <inheritdoc />
    public string Title => "总览";
    /// <inheritdoc />
    public TimeSpan RefreshInterval => TimeSpan.FromSeconds(5);
    /// <inheritdoc />
    public async Task<IRenderable> RenderAsync(GnasApiClient client, CancellationToken cancellationToken)
    {
        try
        {
            using var health = await client.GetAsync("api/health", cancellationToken);
            using var disks = await client.GetAsync("api/disks", cancellationToken);
            using var services = await client.GetAsync("api/services", cancellationToken);
            using var agents = await client.GetAsync("api/agents", cancellationToken);
            using var alerts = await client.GetAsync("api/alerts", cancellationToken);
            var grid = new Grid().AddColumn().AddColumn();
            grid.AddRow(new Panel(health.RootElement.ToString()) { Header = new PanelHeader("健康") }, new Panel(PageHelpers.Table("磁盘", disks.RootElement, "path", "name", "status")));
            grid.AddRow(new Panel(PageHelpers.Table("服务", services.RootElement, "id", "name", "status")), new Panel(PageHelpers.Table("代理", agents.RootElement, "id", "name", "status")));
            grid.AddRow(new Panel(PageHelpers.Table("近期警报", alerts.RootElement, "severity", "message", "createdAt")).Expand(), new Panel(new Markup("按 F1-F7 切换页面，q 退出。")));
            return grid;
        }
        catch (Exception ex) { return PageHelpers.Error(ex); }
    }
    /// <inheritdoc />
    public Task<bool> HandleKeyAsync(ConsoleKeyInfo key, GnasApiClient client, CancellationToken cancellationToken) => Task.FromResult(false);
}
