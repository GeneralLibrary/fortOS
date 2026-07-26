using Spectre.Console.Rendering;
using GNAS.Cli.Tui;
using GNAS.Cli.ApiClient;
using Spectre.Console;

namespace GNAS.Cli.Tui.Pages;

/// <summary>Displays the system overview page.</summary>
public sealed class DashboardPage : ITuiPage
{
    /// <inheritdoc />
    public string Title => "Overview";
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
            grid.AddRow(new Panel(health.RootElement.ToString()) { Header = new PanelHeader("Health") }, new Panel(PageHelpers.Table("Disks", disks.RootElement, "path", "name", "status")));
            grid.AddRow(new Panel(PageHelpers.Table("Services", services.RootElement, "id", "name", "status")), new Panel(PageHelpers.Table("Agents", agents.RootElement, "id", "name", "status")));
            grid.AddRow(new Panel(PageHelpers.Table("Recent alerts", alerts.RootElement, "severity", "message", "createdAt")).Expand(), new Panel(new Markup("Press F1-F7 to switch pages, q to quit.")));
            return grid;
        }
        catch (Exception ex) { return PageHelpers.Error(ex); }
    }
    /// <inheritdoc />
    public Task<bool> HandleKeyAsync(ConsoleKeyInfo key, GnasApiClient client, CancellationToken cancellationToken) => Task.FromResult(false);
}
