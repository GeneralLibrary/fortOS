using Spectre.Console.Rendering;
using GNAS.Cli.Tui;
using System.Text.Json;
using GNAS.Cli.ApiClient;
using Spectre.Console;

namespace GNAS.Cli.Tui.Pages;

/// <summary>Provides shared TUI page utilities.</summary>
internal static class PageHelpers
{
    /// <summary>Create data table.</summary>
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

    /// <summary>Try to get array.</summary>
    public static bool TryArray(JsonElement root, out JsonElement array)
    {
        if (root.ValueKind == JsonValueKind.Array) { array = root; return true; }
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in root.EnumerateObject()) if (p.Value.ValueKind == JsonValueKind.Array) { array = p.Value; return true; }
        }
        array = default; return false;
    }

    /// <summary>Read property text.</summary>
    public static string Get(JsonElement item, string name)
    {
        if (item.ValueKind != JsonValueKind.Object) return item.ToString();
        foreach (var p in item.EnumerateObject()) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? string.Empty : p.Value.ToString();
        return string.Empty;
    }

    /// <summary>Create error fallback panel.</summary>
    public static Panel Error(Exception ex) => new(Markup.Escape(ex is GnasApiException ? ex.Message : "Connection failed: " + ex.Message)) { Header = new PanelHeader("Error") };
}
