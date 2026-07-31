using Spectre.Console.Rendering;
using FortOS.Cli.Tui;
using FortOS.Cli.ApiClient;
using Spectre.Console;

namespace FortOS.Cli.Tui.Pages;

/// <summary>Displays the system settings page.</summary>
public sealed class SettingsPage : ITuiPage
{
    /// <inheritdoc />
    public string Title => "Settings";
    /// <inheritdoc />
    public TimeSpan RefreshInterval => TimeSpan.FromSeconds(15);
    /// <inheritdoc />
    public async Task<IRenderable> RenderAsync(FortOSApiClient client, CancellationToken cancellationToken)
    {
        try { using var doc = await client.GetAsync("api/config", cancellationToken); return new Panel(doc.RootElement.ToString()) { Header = new PanelHeader("Configuration") }; }
        catch (Exception ex) { return PageHelpers.Error(ex); }
    }
    /// <inheritdoc />
    public Task<bool> HandleKeyAsync(ConsoleKeyInfo key, FortOSApiClient client, CancellationToken cancellationToken) => Task.FromResult(false);
}
