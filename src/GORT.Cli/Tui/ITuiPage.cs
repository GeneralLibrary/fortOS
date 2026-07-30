using Spectre.Console.Rendering;
using GORT.Cli.ApiClient;
using Spectre.Console;

namespace GORT.Cli.Tui;

/// <summary>Defines the TUI page contract.</summary>
public interface ITuiPage
{
    /// <summary>Page title.</summary>
    string Title { get; }
    /// <summary>Refresh interval.</summary>
    TimeSpan RefreshInterval { get; }
    /// <summary>Render page.</summary>
    Task<IRenderable> RenderAsync(GortApiClient client, CancellationToken cancellationToken);
    /// <summary>Handle key press; returns true if handled.</summary>
    Task<bool> HandleKeyAsync(ConsoleKeyInfo key, GortApiClient client, CancellationToken cancellationToken);
}
