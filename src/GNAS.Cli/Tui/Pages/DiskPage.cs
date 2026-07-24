using Spectre.Console.Rendering;
using GNAS.Cli.Tui;
using GNAS.Cli.ApiClient;
using Spectre.Console;

namespace GNAS.Cli.Tui.Pages;

/// <summary>显示磁盘页面。</summary>
public sealed class DiskPage : ITuiPage
{
    /// <inheritdoc />
    public string Title => "磁盘";
    /// <inheritdoc />
    public TimeSpan RefreshInterval => TimeSpan.FromSeconds(5);
    /// <inheritdoc />
    public async Task<IRenderable> RenderAsync(GnasApiClient client, CancellationToken cancellationToken)
    {
        try { using var doc = await client.GetAsync("api/disks", cancellationToken); return PageHelpers.Table("磁盘", doc.RootElement, "path", "name", "model", "status", "sizeBytes", "temperatureCelsius"); }
        catch (Exception ex) { return PageHelpers.Error(ex); }
    }
    /// <inheritdoc />
    public Task<bool> HandleKeyAsync(ConsoleKeyInfo key, GnasApiClient client, CancellationToken cancellationToken) => Task.FromResult(false);
}
