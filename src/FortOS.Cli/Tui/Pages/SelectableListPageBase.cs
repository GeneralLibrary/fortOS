using System.Text.Json;
using FortOS.Cli.ApiClient;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace FortOS.Cli.Tui.Pages;

/// <summary>
/// Base class for selectable list pages: fetches a JSON array from an endpoint, renders it as a
/// table with a marker on the selected row, and supports start/stop actions on the selection.
/// Subclasses only provide the endpoint, table title and columns (used both as table headers and
/// as the JSON row property names).
/// The selection is clamped against the current item count on every render and key press, so a
/// list that shrank between refreshes can never index past the end of the array.
/// </summary>
public abstract class SelectableListPageBase : ITuiPage
{
    private int _selected;
    private JsonElement[] _items = [];

    /// <summary>API endpoint returning a JSON array (e.g. "api/services").</summary>
    protected abstract string Endpoint { get; }

    /// <summary>Table title.</summary>
    protected abstract string TableTitle { get; }

    /// <summary>Column names; each value is read from the JSON row property of the same name.</summary>
    protected abstract string[] Columns { get; }

    /// <inheritdoc />
    public abstract string Title { get; }

    /// <inheritdoc />
    public virtual TimeSpan RefreshInterval => TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    public async Task<IRenderable> RenderAsync(FortOSApiClient client, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = await client.GetAsync(Endpoint, cancellationToken).ConfigureAwait(false);
            _items = PageHelpers.TryArray(doc.RootElement, out var arr) ? arr.EnumerateArray().Select(e => e.Clone()).ToArray() : [];
            _selected = Math.Clamp(_selected, 0, Math.Max(0, _items.Length - 1));
            var table = new Table().Title(TableTitle).AddColumn(" ");
            foreach (var column in Columns)
            {
                table.AddColumn(column);
            }

            for (var i = 0; i < _items.Length; i++)
            {
                var cells = Columns.Select(c => Markup.Escape(PageHelpers.Get(_items[i], c))).Prepend(i == _selected ? "▶" : "");
                table.AddRow(cells.ToArray());
            }

            return table;
        }
        catch (Exception ex)
        {
            return PageHelpers.Error(ex);
        }
    }

    /// <inheritdoc />
    public async Task<bool> HandleKeyAsync(ConsoleKeyInfo key, FortOSApiClient client, CancellationToken cancellationToken)
    {
        // Clamp first: the list may have shrunk since the last render while _selected kept its old
        // value, and the action handlers below index _items directly.
        _selected = Math.Clamp(_selected, 0, Math.Max(0, _items.Length - 1));
        if (key.Key == ConsoleKey.UpArrow)
        {
            _selected = Math.Max(0, _selected - 1);
            return true;
        }

        if (key.Key == ConsoleKey.DownArrow)
        {
            _selected = Math.Min(Math.Max(0, _items.Length - 1), _selected + 1);
            return true;
        }

        if ((key.KeyChar is 's' or 'x') && _items.Length > 0)
        {
            var id = PageHelpers.Get(_items[_selected], "id");
            if (!string.IsNullOrWhiteSpace(id))
            {
                var action = key.KeyChar == 's' ? "start" : "stop";
                using (_ = await client.PostAsync($"{Endpoint}/{Uri.EscapeDataString(id)}/{action}", null, cancellationToken).ConfigureAwait(false))
                {
                }
            }

            return true;
        }

        return false;
    }
}
