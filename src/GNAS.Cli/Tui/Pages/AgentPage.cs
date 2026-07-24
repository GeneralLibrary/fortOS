using Spectre.Console.Rendering;
using GNAS.Cli.Tui;
using System.Text.Json;
using GNAS.Cli.ApiClient;
using Spectre.Console;

namespace GNAS.Cli.Tui.Pages;

/// <summary>显示并操作代理页面。</summary>
public sealed class AgentPage : ITuiPage
{
    private int _selected;
    private JsonElement[] _items = [];
    /// <inheritdoc />
    public string Title => "代理（↑↓ 选择，s 启动，x 停止）";
    /// <inheritdoc />
    public TimeSpan RefreshInterval => TimeSpan.FromSeconds(5);
    /// <inheritdoc />
    public async Task<IRenderable> RenderAsync(GnasApiClient client, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = await client.GetAsync("api/agents", cancellationToken);
            _items = PageHelpers.TryArray(doc.RootElement, out var arr) ? arr.EnumerateArray().Select(e => e.Clone()).ToArray() : [];
            var table = new Table().Title("代理").AddColumn(" ").AddColumn("id").AddColumn("name").AddColumn("template").AddColumn("status");
            for (var i = 0; i < _items.Length; i++) table.AddRow(i == _selected ? "▶" : "", Markup.Escape(PageHelpers.Get(_items[i], "id")), Markup.Escape(PageHelpers.Get(_items[i], "name")), Markup.Escape(PageHelpers.Get(_items[i], "template")), Markup.Escape(PageHelpers.Get(_items[i], "status")));
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
            if (!string.IsNullOrWhiteSpace(id)) using (_ = await client.PostAsync($"api/agents/{Uri.EscapeDataString(id)}/{(key.KeyChar == 's' ? "start" : "stop")}", null, cancellationToken)) { }
            return true;
        }
        return false;
    }
}
