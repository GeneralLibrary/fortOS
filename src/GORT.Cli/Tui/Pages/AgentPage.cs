using Spectre.Console.Rendering;
using GORT.Cli.Tui;
using System.Text.Json;
using GORT.Cli.ApiClient;
using Spectre.Console;

namespace GORT.Cli.Tui.Pages;

/// <summary>Displays and operates the agent page.</summary>
public sealed class AgentPage : ITuiPage
{
    private int _selected;
    private JsonElement[] _items = [];
    /// <inheritdoc />
    public string Title => "Agents (↑↓ select, s start, x stop)";
    /// <inheritdoc />
    public TimeSpan RefreshInterval => TimeSpan.FromSeconds(5);
    /// <inheritdoc />
    public async Task<IRenderable> RenderAsync(GortApiClient client, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = await client.GetAsync("api/agents", cancellationToken);
            _items = PageHelpers.TryArray(doc.RootElement, out var arr) ? arr.EnumerateArray().Select(e => e.Clone()).ToArray() : [];
            var table = new Table().Title("Agents").AddColumn(" ").AddColumn("id").AddColumn("name").AddColumn("template").AddColumn("status");
            for (var i = 0; i < _items.Length; i++) table.AddRow(i == _selected ? "▶" : "", Markup.Escape(PageHelpers.Get(_items[i], "id")), Markup.Escape(PageHelpers.Get(_items[i], "name")), Markup.Escape(PageHelpers.Get(_items[i], "template")), Markup.Escape(PageHelpers.Get(_items[i], "status")));
            return table;
        }
        catch (Exception ex) { return PageHelpers.Error(ex); }
    }
    /// <inheritdoc />
    public async Task<bool> HandleKeyAsync(ConsoleKeyInfo key, GortApiClient client, CancellationToken cancellationToken)
    {
        if (key.Key == ConsoleKey.UpArrow) { _selected = Math.Max(0, _selected - 1); return true; }
        if (key.Key == ConsoleKey.DownArrow) { _selected = Math.Min(Math.Max(0, _items.Length - 1), _selected + 1); return true; }
        if ((key.KeyChar is 's' or 'x') && _items.Length > 0)
        {
            var id = PageHelpers.Get(_items[_selected], "id");
            if (!string.IsNullOrWhiteSpace(id)) using (_ = await client.PostAsync($"api/agents/{Uri.EscapeDataString(id)}/{(key.KeyChar == 's' ? "start" : "stop")}", null, cancellationToken)) { }
            return true;
        }
        return false;
    }
}
