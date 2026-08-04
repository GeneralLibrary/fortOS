using Spectre.Console.Rendering;
using FortOS.Cli.Tui;
using FortOS.Cli.ApiClient;
using Spectre.Console;

namespace FortOS.Cli.Tui.Pages;

/// <summary>Displays the log page.</summary>
public sealed class LogPage : ITuiPage
{
    private string? _category;
    private string? _search;
    private readonly string?[] _levels = [null, "Debug", "Information", "Warning", "Error", "Critical"];
    private int _levelIndex;
    private readonly TimeSpan[] _refresh = [TimeSpan.FromDays(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];
    private int _refreshIndex = 2;
    /// <inheritdoc />
    public string Title => "Logs (S/A/C/G/T/M category, l level, / search, r refresh)";
    /// <inheritdoc />
    public TimeSpan RefreshInterval => _refresh[_refreshIndex];
    /// <inheritdoc />
    public async Task<IRenderable> RenderAsync(FortOSApiClient client, CancellationToken cancellationToken)
    {
        try
        {
            var q = new List<string>();
            if (_category is not null) q.Add("category=" + Uri.EscapeDataString(_category));
            if (_levels[_levelIndex] is not null) q.Add("level=" + Uri.EscapeDataString(_levels[_levelIndex]!));
            if (_search is not null) q.Add("q=" + Uri.EscapeDataString(_search));
            using var doc = await client.GetAsync("api/logs" + (q.Count == 0 ? string.Empty : "?" + string.Join('&', q)), cancellationToken);
            var table = PageHelpers.Table($"Logs category={_category ?? "All"} level={_levels[_levelIndex] ?? "All"} Refresh={(RefreshInterval.TotalHours > 1 ? "Off" : RefreshInterval.TotalSeconds + "s")}", doc.RootElement, "timestamp", "level", "category", "message");
            return table;
        }
        catch (Exception ex) { return PageHelpers.Error(ex); }
    }
    /// <inheritdoc />
    public Task<bool> HandleKeyAsync(ConsoleKeyInfo key, FortOSApiClient client, CancellationToken cancellationToken)
    {
        var map = new Dictionary<char, string> { ['S'] = "System", ['A'] = "Audit", ['C'] = "Container", ['G'] = "FortOS", ['T'] = "Task", ['M'] = "Metrics" };
        if (map.TryGetValue(char.ToUpperInvariant(key.KeyChar), out var category)) { _category = _category == category ? null : category; return Task.FromResult(true); }
        // Normalize to lower case so the shortcuts work regardless of Caps Lock / Shift state,
        // consistent with the category mapping above.
        switch (char.ToLowerInvariant(key.KeyChar))
        {
            case 'l': _levelIndex = (_levelIndex + 1) % _levels.Length; return Task.FromResult(true);
            case 'r': _refreshIndex = (_refreshIndex + 1) % _refresh.Length; return Task.FromResult(true);
            case '/': _search = AnsiConsole.Ask<string>("Search text (leave blank to clear):", string.Empty); if (string.IsNullOrWhiteSpace(_search)) _search = null; return Task.FromResult(true);
            default: return Task.FromResult(false);
        }
    }
}
