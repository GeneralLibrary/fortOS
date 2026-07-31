using System.CommandLine;

namespace FortOS.Cli.Commands;

/// <summary>Register service commands.</summary>
public static class ServiceCommand
{
    /// <summary>Create service command.</summary>
    public static Command Create(CliOptions options)
    {
        var root = new Command("service", "Service management");
        var list = new Command("list", "List services");
        list.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.GetAsync("api/services", t), cancellationToken: ct));
        root.Add(list);
        foreach (var verb in new[] { "start", "stop", "restart" })
        {
            var id = new Argument<string>("id"); var cmd = new Command(verb, $"{verb} service") { id };
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
