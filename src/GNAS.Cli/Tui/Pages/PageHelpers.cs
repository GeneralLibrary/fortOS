using Spectre.Console.Rendering;
using GNAS.Cli.Tui;
using System.Text.Json;
using GNAS.Cli.ApiClient;
using Spectre.Console;

namespace GNAS.Cli.Tui.Pages;

/// <summary>提供 TUI 页面共用工具。</summary>
internal static class PageHelpers
{
    /// <summary>建立资料表。</summary>
    public static Table Table(string title, JsonElement root, params string[] columns)
    {
        var table = new Table().Title(title).RoundedBorder();
        foreach (var c in columns) table.AddColumn(c);
        if (TryArray(root, out var array))
        {
            foreach (var item in array.EnumerateArray().Take(30))
                table.AddRow(columns.Select(c => Markup.Escape(Get(item, c))).ToArray());
        }
        return table;
    }

    /// <summary>尝试取得数组。</summary>
    public static bool TryArray(JsonElement root, out JsonElement array)
    {
        if (root.ValueKind == JsonValueKind.Array) { array = root; return true; }
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in root.EnumerateObject()) if (p.Value.ValueKind == JsonValueKind.Array) { array = p.Value; return true; }
        }
        array = default; return false;
    }

    /// <summary>读取属性文字。</summary>
    public static string Get(JsonElement item, string name)
    {
        if (item.ValueKind != JsonValueKind.Object) return item.ToString();
        foreach (var p in item.EnumerateObject()) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? string.Empty : p.Value.ToString();
        return string.Empty;
    }

    /// <summary>建立错误降级面板。</summary>
    public static Panel Error(Exception ex) => new(Markup.Escape(ex is GnasApiException ? ex.Message : "无法连接：" + ex.Message)) { Header = new PanelHeader("无法连接") };
}
