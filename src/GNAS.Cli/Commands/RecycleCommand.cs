using System.CommandLine;

namespace GNAS.Cli.Commands;

/// <summary>注册回收站命令。</summary>
public static class RecycleCommand
{
    /// <summary>建立 recycle 命令。</summary>
    public static Command Create(CliOptions options)
    {
        var root = new Command("recycle", "回收站管理");
        Command AddShareCommand(string name, string desc, Func<ParseResult, GNAS.Cli.ApiClient.GnasApiClient, CancellationToken, Task<System.Text.Json.JsonDocument>> action)
        {
            var share = new Argument<string>("share"); var cmd = new Command(name, desc) { share };
            cmd.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => action(p, c, t), cancellationToken: ct));
            return cmd;
        }
        root.Add(AddShareCommand("list", "列出回收站", (p, c, t) => c.GetAsync($"api/recycle/{Uri.EscapeDataString(p.GetRequiredValue<string>("share"))}", t)));
        var shareArg = new Argument<string>("share"); var idArg = new Argument<string>("id");
        var target = new Option<string?>("--target") { Description = "还原目标路径（不传则按原路径恢复）" };
        var restore = new Command("restore", "还原项目") { shareArg, idArg, target };
        restore.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.PostAsync(
            $"api/recycle/{Uri.EscapeDataString(p.GetRequiredValue(shareArg))}/restore/{Uri.EscapeDataString(p.GetRequiredValue(idArg))}",
            p.GetValue(target) is { Length: > 0 } x ? new { targetPath = x } : null,
            t), cancellationToken: ct));
        var emptyShare = new Argument<string>("share"); var confirm = new Option<bool>("--confirm");
        var empty = new Command("empty", "清空回收站") { emptyShare, confirm };
        empty.SetAction((p, ct) => CommandRuntime.RequireConfirm(p.GetValue(confirm))
            ? CommandRuntime.RunAsync(p, options, (c, t) => c.DeleteAsync($"api/recycle/{Uri.EscapeDataString(p.GetRequiredValue(emptyShare))}/empty", t), cancellationToken: ct)
            : Task.FromResult(2));
        root.Add(restore); root.Add(empty); root.Add(AddShareCommand("config", "显示回收站配置", (p, c, t) => c.GetAsync($"api/recycle/{Uri.EscapeDataString(p.GetRequiredValue<string>("share"))}", t)));
        return root;
    }
}
