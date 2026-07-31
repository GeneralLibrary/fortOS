using Spectre.Console.Rendering;
using FortOS.Cli.Tui;
using FortOS.Cli.ApiClient;
using Spectre.Console;

namespace FortOS.Cli.Tui.Pages;

/// <summary>Displays the disk page.</summary>
public sealed class DiskPage : ITuiPage
{
    /// <inheritdoc />
    public string Title => "Disks";
    /// <inheritdoc />
    public TimeSpan RefreshInterval => TimeSpan.FromSeconds(5);
    /// <inheritdoc />
    public async Task<IRenderable> RenderAsync(FortOSApiClient client, CancellationToken cancellationToken)
    {
        try { using var doc = await client.GetAsync("api/disks", cancellationToken); return PageHelpers.Table("Disks", doc.RootElement, "path", "name", "model", "status", "sizeBytes", "temperatureCelsius"); }
        catch (Exception ex) { return PageHelpers.Error(ex); }
    }
    /// <inheritdoc />
    public Task<bool> HandleKeyAsync(ConsoleKeyInfo key, FortOSApiClient client, CancellationToken cancellationToken) => Task.FromResult(false);
}
