using Spectre.Console.Rendering;
using GNAS.Cli.Tui;
using System.Text.Json;
using GNAS.Cli.ApiClient;
using Spectre.Console;

namespace GNAS.Cli.Tui.Pages;

/// <summary>Displays and operates the service page.</summary>
public sealed class ServicePage : ITuiPage
{
    private int _selected;
    private JsonElement[] _items = [];
    /// <inheritdoc />
    public string Title => "Services (↑↓ select, s start, x stop)";
    /// <inheritdoc />
    public TimeSpan RefreshInterval => TimeSpan.FromSeconds(5);
    /// <inheritdoc />
    public async Task<IRenderable> RenderAsync(GnasApiClient client, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = await client.GetAsync("api/services", cancellationToken);
            _items = PageHelpers.TryArray(doc.RootElement, out var arr) ? arr.EnumerateArray().Select(e => e.Clone()).ToArray() : [];
            var table = new Table().Title("Services").AddColumn(" ").AddColumn("id").AddColumn("name").AddColumn("status");
            for (var i = 0; i < _items.Length; i++) table.AddRow(i == _selected ? "▶" : "", Markup.Escape(PageHelpers.Get(_items[i], "id")), Markup.Escape(PageHelpers.Get(_items[i], "name")), Markup.Escape(PageHelpers.Get(_items[i], "status")));
            return table;
        }
        catch (Exception ex) { return PageHelpers.Error(ex); }
    }
    /// <inheritdoc />
    public async Task<bool> HandleKeyAsync(ConsoleKeyInfo key, GnasApiClient client, CancellationToken cancellationToken)
    {
        if (key.Key == ConsoleKey.UpArrow) { _selected = Math.Max(0, _selected - 1); return true; }
        if (key.Key == ConsoleKey.DownArrow) { _selected = Math.Min(Math.Max(0, _items.Length - 1), _selected + 1); return true; }
        if ((key.KeyChar is 's' or 'x') && _items.Length > 0)
        {
            var id = PageHelpers.Get(_items[_selected], "id");
            if (!string.IsNullOrWhiteSpace(id)) using (_ = await client.PostAsync($"api/services/{Uri.EscapeDataString(id)}/{(key.KeyChar == 's' ? "start" : "stop")}", null, cancellationToken)) { }
            return true;
        }
        return false;
    }
}
