using System.CommandLine;

namespace GNAS.Cli.Commands;

/// <summary>注册快照命令。</summary>
public static class SnapshotCommand
{
    /// <summary>建立 snapshot 命令。</summary>
    public static Command Create(CliOptions options)
    {
        var root = new Command("snapshot", "快照管理");
        var target = new Argument<string>("target");
        var create = new Command("create", "建立快照") { target };
        create.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.PostAsync("api/snapshots", new { target = p.GetRequiredValue(target) }, t), cancellationToken: ct));
        var targetOpt = new Argument<string?>("target") { Arity = ArgumentArity.ZeroOrOne };
        var list = new Command("list", "列出快照") { targetOpt };
        list.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.GetAsync("api/snapshots" + (p.GetValue(targetOpt) is { } x ? $"?target={Uri.EscapeDataString(x)}" : string.Empty), t), cancellationToken: ct));
        var id = new Argument<string>("id"); var confirm = new Option<bool>("--confirm");
        var restore = new Command("restore", "还原快照") { id, confirm };
        restore.SetAction((p, ct) => CommandRuntime.RequireConfirm(p.GetValue(confirm)) ? CommandRuntime.RunAsync(p, options, (c, t) => c.PostAsync($"api/snapshots/{Uri.EscapeDataString(p.GetRequiredValue(id))}/restore", null, t), cancellationToken: ct) : Task.FromResult(2));
        root.Add(create); root.Add(list); root.Add(restore);
        return root;
    }
}
