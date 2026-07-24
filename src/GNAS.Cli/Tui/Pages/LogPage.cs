using Spectre.Console.Rendering;
using GNAS.Cli.Tui;
using GNAS.Cli.ApiClient;
using Spectre.Console;

namespace GNAS.Cli.Tui.Pages;

/// <summary>显示日志页面。</summary>
public sealed class LogPage : ITuiPage
{
    private string? _category;
    private string? _search;
    private readonly string?[] _levels = [null, "Debug", "Information", "Warning", "Error", "Critical"];
    private int _levelIndex;
    private readonly TimeSpan[] _refresh = [TimeSpan.FromDays(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];
    private int _refreshIndex = 2;
    /// <inheritdoc />
    public string Title => "日志（S/A/C/G/T/M 分类，l 级别，/ 搜索，r 刷新）";
    /// <inheritdoc />
    public TimeSpan RefreshInterval => _refresh[_refreshIndex];
    /// <inheritdoc />
    public async Task<IRenderable> RenderAsync(GnasApiClient client, CancellationToken cancellationToken)
    {
        try
        {
            var q = new List<string>();
            if (_category is not null) q.Add("category=" + Uri.EscapeDataString(_category));
            if (_levels[_levelIndex] is not null) q.Add("level=" + Uri.EscapeDataString(_levels[_levelIndex]!));
            if (_search is not null) q.Add("q=" + Uri.EscapeDataString(_search));
            using var doc = await client.GetAsync("api/logs" + (q.Count == 0 ? string.Empty : "?" + string.Join('&', q)), cancellationToken);
            var table = PageHelpers.Table($"日志 分类={_category ?? "全部"} 级别={_levels[_levelIndex] ?? "全部"} 刷新={(RefreshInterval.TotalHours > 1 ? "关闭" : RefreshInterval.TotalSeconds + "s")}", doc.RootElement, "timestamp", "level", "category", "message");
            return table;
        }
        catch (Exception ex) { return PageHelpers.Error(ex); }
    }
    /// <inheritdoc />
    public Task<bool> HandleKeyAsync(ConsoleKeyInfo key, GnasApiClient client, CancellationToken cancellationToken)
    {
        var map = new Dictionary<char, string> { ['S'] = "System", ['A'] = "Audit", ['C'] = "Container", ['G'] = "Gnas", ['T'] = "Task", ['M'] = "Metrics" };
        if (map.TryGetValue(char.ToUpperInvariant(key.KeyChar), out var category)) { _category = _category == category ? null : category; return Task.FromResult(true); }
        if (key.KeyChar == 'l') { _levelIndex = (_levelIndex + 1) % _levels.Length; return Task.FromResult(true); }
        if (key.KeyChar == 'r') { _refreshIndex = (_refreshIndex + 1) % _refresh.Length; return Task.FromResult(true); }
        if (key.KeyChar == '/') { _search = AnsiConsole.Ask<string>("搜索文字（空白清除）：", string.Empty); if (string.IsNullOrWhiteSpace(_search)) _search = null; return Task.FromResult(true); }
        return Task.FromResult(false);
    }
}
