using Spectre.Console.Rendering;
using GNAS.Cli.ApiClient;
using Spectre.Console;

namespace GNAS.Cli.Tui;

/// <summary>Defines the TUI page contract.</summary>
public interface ITuiPage
{
    /// <summary>Page title.</summary>
    string Title { get; }
    /// <summary>Refresh interval.</summary>
    TimeSpan RefreshInterval { get; }
    /// <summary>Render page.</summary>
    Task<IRenderable> RenderAsync(GnasApiClient client, CancellationToken cancellationToken);
    /// <summary>Handle key press; returns true if handled.</summary>
    Task<bool> HandleKeyAsync(ConsoleKeyInfo key, GnasApiClient client, CancellationToken cancellationToken);
}
