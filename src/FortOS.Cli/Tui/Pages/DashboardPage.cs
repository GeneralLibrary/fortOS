using Spectre.Console.Rendering;
using FortOS.Cli.Tui;
using FortOS.Cli.ApiClient;
using Spectre.Console;
using System.Text.Json;

namespace FortOS.Cli.Tui.Pages;

/// <summary>Displays the system overview page.</summary>
public sealed class DashboardPage : ITuiPage
{
    /// <inheritdoc />
    public string Title => "Overview";
    /// <inheritdoc />
    public TimeSpan RefreshInterval => TimeSpan.FromSeconds(5);
    /// <inheritdoc />
    public async Task<IRenderable> RenderAsync(FortOSApiClient client, CancellationToken cancellationToken)
    {
        try
        {
            using var health = await client.GetAsync("api/health", cancellationToken);
            using var metrics = await client.GetAsync("api/metrics/system", cancellationToken);
            using var disks = await client.GetAsync("api/disks", cancellationToken);
            using var services = await client.GetAsync("api/services", cancellationToken);
            using var agents = await client.GetAsync("api/agents", cancellationToken);
            using var alerts = await client.GetAsync("api/alerts", cancellationToken);
            var grid = new Grid().AddColumn().AddColumn();
            grid.AddRow(new Panel(health.RootElement.ToString()) { Header = new PanelHeader("Health") }, new Panel(LiveSummary(metrics.RootElement)) { Header = new PanelHeader("Live metrics") });
            grid.AddRow(new Panel(PageHelpers.Table("Disks", disks.RootElement, "path", "name", "status")), new Panel(PageHelpers.Table("Services", services.RootElement, "id", "name", "status")));
            grid.AddRow(new Panel(PageHelpers.Table("Agents", agents.RootElement, "id", "name", "status")), new Panel(PageHelpers.Table("Recent alerts", alerts.RootElement, "severity", "message", "createdAt")).Expand());
            return grid;
        }
        catch (Exception ex) { return PageHelpers.Error(ex); }
    }

    private static Markup LiveSummary(JsonElement metrics)
    {
        var host = metrics.GetProperty("host");
        var cpu = metrics.GetProperty("cpu");
        var memory = metrics.GetProperty("memory");
        var network = metrics.GetProperty("networks").EnumerateArray()
            .Where(item => item.GetProperty("isUp").GetBoolean())
            .Sum(item => item.GetProperty("receiveBytesPerSecond").GetDouble() + item.GetProperty("transmitBytesPerSecond").GetDouble());
        return new Markup(
            $"[bold]Uptime[/] {Markup.Escape(host.GetProperty("uptime").ToString())}\n" +
            $"[bold]CPU[/] {cpu.GetProperty("usagePercent").GetDouble():0.0}%  " +
            $"[bold]Memory[/] {memory.GetProperty("usedPercent").GetDouble():0.0}%\n" +
            $"[bold]Network traffic[/] {network / 1024 / 1024:0.00} MiB/s  " +
            $"[bold]Diagnostics[/] {metrics.GetProperty("diagnostics").GetArrayLength()}");
    }

    /// <inheritdoc />
    public Task<bool> HandleKeyAsync(ConsoleKeyInfo key, FortOSApiClient client, CancellationToken cancellationToken) => Task.FromResult(false);
}
