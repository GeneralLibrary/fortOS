using System.CommandLine;
using System.Web;

namespace GNAS.Cli.Commands;

/// <summary>注册磁盘命令。</summary>
public static class DiskCommand
{
    /// <summary>建立 disk 命令。</summary>
    public static Command Create(CliOptions options)
    {
        var disk = new Command("disk", "磁盘管理");
        var list = new Command("list", "列出磁盘");
        list.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.GetAsync("api/disks", t), cancellationToken: ct));
        var pathArg = new Argument<string>("path") { Description = "磁盘路径" };
        var info = new Command("info", "显示磁盘详情") { pathArg };
        info.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.GetAsync($"api/disks?path={HttpUtility.UrlEncode(p.GetRequiredValue(pathArg))}", t), cancellationToken: ct));
        var smart = new Command("smart", "执行 SMART 检查") { pathArg };
        smart.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.PostAsync("api/disks/smart-check", new { path = p.GetRequiredValue(pathArg) }, t), cancellationToken: ct));
        disk.Add(list); disk.Add(info); disk.Add(smart);
        return disk;
    }
}
