using System.CommandLine;

namespace GNAS.Cli.Commands;

/// <summary>注册服务命令。</summary>
public static class ServiceCommand
{
    /// <summary>建立 service 命令。</summary>
    public static Command Create(CliOptions options)
    {
        var root = new Command("service", "服务管理");
        var list = new Command("list", "列出服务");
        list.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.GetAsync("api/services", t), cancellationToken: ct));
        root.Add(list);
        foreach (var verb in new[] { "start", "stop", "restart" })
        {
            var id = new Argument<string>("id"); var cmd = new Command(verb, $"{verb} 服务") { id };
            cmd.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, async (c, t) =>
            {
                if (verb == "restart")
                {
                    await c.PostAsync($"api/services/{Uri.EscapeDataString(p.GetRequiredValue(id))}/stop", null, t);
                    return await c.PostAsync($"api/services/{Uri.EscapeDataString(p.GetRequiredValue(id))}/start", null, t);
                }
                return await c.PostAsync($"api/services/{Uri.EscapeDataString(p.GetRequiredValue(id))}/{verb}", null, t);
            }, cancellationToken: ct));
            root.Add(cmd);
        }
        return root;
    }
}
