using Spectre.Console.Rendering;
using FortOS.Cli.ApiClient;
using Spectre.Console;

namespace FortOS.Cli.Tui;

/// <summary>Defines the TUI page contract.</summary>
public interface ITuiPage
{
    /// <summary>Page title.</summary>
    string Title { get; }
    /// <summary>Refresh interval.</summary>
    TimeSpan RefreshInterval { get; }
    /// <summary>Render page.</summary>
    Task<IRenderable> RenderAsync(FortOSApiClient client, CancellationToken cancellationToken);
    /// <summary>Handle key press; returns true if handled.</summary>
    Task<bool> HandleKeyAsync(ConsoleKeyInfo key, FortOSApiClient client, CancellationToken cancellationToken);
}
