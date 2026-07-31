using Spectre.Console.Rendering;
using FortOS.Cli.Tui;
using FortOS.Cli.ApiClient;
using Spectre.Console;

namespace FortOS.Cli.Tui.Pages;

/// <summary>Displays the alert page.</summary>
public sealed class AlertPage : ITuiPage
{
    /// <inheritdoc />
    public string Title => "Alerts";
    /// <inheritdoc />
    public TimeSpan RefreshInterval => TimeSpan.FromSeconds(10);
    /// <inheritdoc />
    public async Task<IRenderable> RenderAsync(FortOSApiClient client, CancellationToken cancellationToken)
    {
        try { using var doc = await client.GetAsync("api/alerts", cancellationToken); return PageHelpers.Table("Alerts", doc.RootElement, "id", "severity", "message", "createdAt", "acknowledged"); }
        catch (Exception ex) { return PageHelpers.Error(ex); }
    }
    /// <inheritdoc />
    public Task<bool> HandleKeyAsync(ConsoleKeyInfo key, FortOSApiClient client, CancellationToken cancellationToken) => Task.FromResult(false);
}
