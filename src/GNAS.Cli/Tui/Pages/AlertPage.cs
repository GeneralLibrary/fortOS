using Spectre.Console.Rendering;
using GNAS.Cli.Tui;
using GNAS.Cli.ApiClient;
using Spectre.Console;

namespace GNAS.Cli.Tui.Pages;

/// <summary>Displays the alert page.</summary>
public sealed class AlertPage : ITuiPage
{
    /// <inheritdoc />
    public string Title => "Alerts";
    /// <inheritdoc />
    public TimeSpan RefreshInterval => TimeSpan.FromSeconds(10);
    /// <inheritdoc />
    public async Task<IRenderable> RenderAsync(GnasApiClient client, CancellationToken cancellationToken)
    {
        try { using var doc = await client.GetAsync("api/alerts", cancellationToken); return PageHelpers.Table("Alerts", doc.RootElement, "id", "severity", "message", "createdAt", "acknowledged"); }
        catch (Exception ex) { return PageHelpers.Error(ex); }
    }
    /// <inheritdoc />
    public Task<bool> HandleKeyAsync(ConsoleKeyInfo key, GnasApiClient client, CancellationToken cancellationToken) => Task.FromResult(false);
}
