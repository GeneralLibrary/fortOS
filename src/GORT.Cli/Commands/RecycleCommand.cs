using System.CommandLine;

namespace GORT.Cli.Commands;

/// <summary>Register recycle bin commands.</summary>
public static class RecycleCommand
{
    /// <summary>Create recycle command.</summary>
    public static Command Create(CliOptions options)
    {
        var root = new Command("recycle", "Recycle bin management");
        Command AddShareCommand(string name, string desc, Func<ParseResult, GORT.Cli.ApiClient.GortApiClient, CancellationToken, Task<System.Text.Json.JsonDocument>> action)
        {
            var share = new Argument<string>("share"); var cmd = new Command(name, desc) { share };
            cmd.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => action(p, c, t), cancellationToken: ct));
            return cmd;
        }
        root.Add(AddShareCommand("list", "List recycle bin", (p, c, t) => c.GetAsync($"api/recycle/{Uri.EscapeDataString(p.GetRequiredValue<string>("share"))}", t)));
        var shareArg = new Argument<string>("share"); var idArg = new Argument<string>("id");
        var target = new Option<string?>("--target") { Description = "Restore target path (defaults to original path)" };
        var restore = new Command("restore", "Restore item") { shareArg, idArg, target };
        restore.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.PostAsync(
            $"api/recycle/{Uri.EscapeDataString(p.GetRequiredValue(shareArg))}/restore/{Uri.EscapeDataString(p.GetRequiredValue(idArg))}",
            p.GetValue(target) is { Length: > 0 } x ? new { targetPath = x } : null,
            t), cancellationToken: ct));
        var emptyShare = new Argument<string>("share"); var confirm = new Option<bool>("--confirm");
        var empty = new Command("empty", "Empty recycle bin") { emptyShare, confirm };
        empty.SetAction((p, ct) => CommandRuntime.RequireConfirm(p.GetValue(confirm))
            ? CommandRuntime.RunAsync(p, options, (c, t) => c.DeleteAsync($"api/recycle/{Uri.EscapeDataString(p.GetRequiredValue(emptyShare))}/empty", t), cancellationToken: ct)
            : Task.FromResult(2));
        root.Add(restore); root.Add(empty); root.Add(AddShareCommand("config", "Show recycle bin configuration", (p, c, t) => c.GetAsync($"api/recycle/{Uri.EscapeDataString(p.GetRequiredValue<string>("share"))}", t)));
        return root;
    }
}
