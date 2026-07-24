using System.CommandLine;

namespace GNAS.Cli.Commands;

/// <summary>注册共享目录命令。</summary>
public static class ShareCommand
{
    /// <summary>建立 share 命令。</summary>
    public static Command Create(CliOptions options)
    {
        var root = new Command("share", "共享目录管理");
        var list = new Command("list", "列出共享");
        list.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.GetAsync("api/shares", t), cancellationToken: ct));
        var name = new Argument<string>("name"); var path = new Argument<string>("path");
        var protocols = new Option<string?>("--protocols") { Description = "协议列表，例如 smb,nfs" };
        var readOnly = new Option<bool>("--read-only") { Description = "只读共享" };
        var create = new Command("create", "建立共享") { name, path, protocols, readOnly };
        create.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.PostAsync("api/shares", new { name = p.GetRequiredValue(name), path = p.GetRequiredValue(path), protocols = (p.GetValue(protocols) ?? "smb").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), readOnly = p.GetValue(readOnly) }, t), cancellationToken: ct));
        var id = new Argument<string>("id"); var confirm = new Option<bool>("--confirm");
        var delete = new Command("delete", "删除共享") { id, confirm };
        delete.SetAction((p, ct) => CommandRuntime.RequireConfirm(p.GetValue(confirm)) ? CommandRuntime.RunMessageAsync(p, options, (c, t) => c.DeleteAsync($"api/shares/{Uri.EscapeDataString(p.GetRequiredValue(id))}", t), "共享已删除", ct) : Task.FromResult(2));
        root.Add(list); root.Add(create); root.Add(delete);
        return root;
    }
}
