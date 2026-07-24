using System.CommandLine;

namespace GNAS.Cli.Commands;

/// <summary>注册代理命令。</summary>
public static class AgentCommand
{
    /// <summary>建立 agent 命令。</summary>
    public static Command Create(CliOptions options)
    {
        var root = new Command("agent", "应用代理管理");
        var list = new Command("list", "列出代理");
        list.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.GetAsync("api/agents", t), cancellationToken: ct));
        var catalog = new Command("catalog", "列出模板");
        catalog.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.GetAsync("api/agents/catalog", t), cancellationToken: ct));
        var template = new Argument<string>("template"); var param = new Option<string[]>("--param") { Arity = ArgumentArity.ZeroOrMore, AllowMultipleArgumentsPerToken = false, Description = "模板参数 k=v，可重复" };
        var deploy = new Command("deploy", "部署代理") { template, param };
        deploy.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.PostAsync("api/agents", new { template = p.GetRequiredValue(template), parameters = CommandRuntime.ParsePairs(p.GetValue(param)) }, t), cancellationToken: ct));
        root.Add(list); root.Add(deploy); root.Add(catalog);
        foreach (var verb in new[] { "start", "stop" })
        {
            var id = new Argument<string>("id"); var cmd = new Command(verb, $"{verb} 代理") { id };
            cmd.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.PostAsync($"api/agents/{Uri.EscapeDataString(p.GetRequiredValue(id))}/{verb}", null, t), cancellationToken: ct));
            root.Add(cmd);
        }
        var logId = new Argument<string>("id"); var follow = new Option<bool>("--follow");
        var logs = new Command("logs", "查看代理日志") { logId, follow };
        logs.SetAction(async (p, ct) =>
        {
            if (!p.GetValue(follow)) return await CommandRuntime.RunAsync(p, options, (c, t) => c.GetAsync($"api/agents/{Uri.EscapeDataString(p.GetRequiredValue(logId))}/logs", t), cancellationToken: ct);
            try { using var c = CommandRuntime.Client(p, options); await foreach (var line in c.GetSseStreamAsync($"api/agents/{Uri.EscapeDataString(p.GetRequiredValue(logId))}/logs", ct)) Console.WriteLine(line); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
        });
        var removeId = new Argument<string>("id"); var confirm = new Option<bool>("--confirm");
        var remove = new Command("remove", "移除代理") { removeId, confirm };
        remove.SetAction((p, ct) => CommandRuntime.RequireConfirm(p.GetValue(confirm)) ? CommandRuntime.RunAsync(p, options, (c, t) => c.DeleteAsync($"api/agents/{Uri.EscapeDataString(p.GetRequiredValue(removeId))}", t), cancellationToken: ct) : Task.FromResult(2));
        root.Add(logs); root.Add(remove);
        return root;
    }
}
