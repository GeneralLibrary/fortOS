using System.CommandLine;
using System.Web;

namespace GORT.Cli.Commands;

/// <summary>Register disk commands.</summary>
public static class DiskCommand
{
    /// <summary>Create disk command.</summary>
    public static Command Create(CliOptions options)
    {
        var disk = new Command("disk", "Disk management");
        var list = new Command("list", "List disks");
        list.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.GetAsync("api/disks", t), cancellationToken: ct));
        var pathArg = new Argument<string>("path") { Description = "Disk path" };
        var info = new Command("info", "Show disk details") { pathArg };
        info.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.GetAsync($"api/disks?path={HttpUtility.UrlEncode(p.GetRequiredValue(pathArg))}", t), cancellationToken: ct));
        var smart = new Command("smart", "Execute SMART check") { pathArg };
        smart.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.PostAsync("api/disks/smart-check", new { path = p.GetRequiredValue(pathArg) }, t), cancellationToken: ct));
        disk.Add(list); disk.Add(info); disk.Add(smart);
        return disk;
    }
}
