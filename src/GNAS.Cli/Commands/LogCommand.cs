using System.CommandLine;
using System.Web;

namespace GNAS.Cli.Commands;

/// <summary>注册日志命令。</summary>
public static class LogCommand
{
    /// <summary>建立 log 命令。</summary>
    public static Command Create(CliOptions options)
    {
        var root = new Command("log", "日志查询");
        var follow = new Option<bool>("--follow"); var category = new Option<string?>("--category"); var level = new Option<string?>("--level");
        var view = new Command("view", "查看日志") { follow, category, level };
        view.SetAction(async (p, ct) =>
        {
            var query = BuildQuery(p.GetValue(category), p.GetValue(level), null);
            if (!p.GetValue(follow)) return await CommandRuntime.RunAsync(p, options, (c, t) => c.GetAsync("api/logs" + query, t), cancellationToken: ct);
            try { using var c = CommandRuntime.Client(p, options); await foreach (var line in c.GetSseStreamAsync("api/logs/stream" + query, ct)) Console.WriteLine(line); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
        });
        var text = new Argument<string>("text"); var queryCmd = new Command("query", "查询日志") { text };
        queryCmd.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.GetAsync("api/logs" + BuildQuery(null, null, p.GetRequiredValue(text)), t), cancellationToken: ct));
        root.Add(view); root.Add(queryCmd); return root;
    }

    private static string BuildQuery(string? category, string? level, string? text)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(category)) parts.Add("category=" + HttpUtility.UrlEncode(category));
        if (!string.IsNullOrWhiteSpace(level)) parts.Add("level=" + HttpUtility.UrlEncode(level));
        if (!string.IsNullOrWhiteSpace(text)) parts.Add("q=" + HttpUtility.UrlEncode(text));
        return parts.Count == 0 ? string.Empty : "?" + string.Join('&', parts);
    }
}
