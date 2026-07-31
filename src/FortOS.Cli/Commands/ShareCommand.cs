using System.CommandLine;

namespace FortOS.Cli.Commands;

/// <summary>Register share directory commands.</summary>
public static class ShareCommand
{
    /// <summary>Create share command.</summary>
    public static Command Create(CliOptions options)
    {
        var root = new Command("share", "Share management");
        var list = new Command("list", "List shares");
        list.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.GetAsync("api/shares", t), cancellationToken: ct));
        var name = new Argument<string>("name"); var path = new Argument<string>("path");
        var protocols = new Option<string?>("--protocols") { Description = "Protocol list, e.g. smb,nfs" };
        var readOnly = new Option<bool>("--read-only") { Description = "Read-only share" };
        var create = new Command("create", "Create share") { name, path, protocols, readOnly };
        create.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.PostAsync("api/shares", new { name = p.GetRequiredValue(name), path = p.GetRequiredValue(path), protocols = (p.GetValue(protocols) ?? "smb").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), readOnly = p.GetValue(readOnly) }, t), cancellationToken: ct));
        var id = new Argument<string>("id"); var confirm = new Option<bool>("--confirm");
        var delete = new Command("delete", "Delete share") { id, confirm };
        delete.SetAction((p, ct) => CommandRuntime.RequireConfirm(p.GetValue(confirm)) ? CommandRuntime.RunMessageAsync(p, options, (c, t) => c.DeleteAsync($"api/shares/{Uri.EscapeDataString(p.GetRequiredValue(id))}", t), "Share deleted", ct) : Task.FromResult(2));
        root.Add(list); root.Add(create); root.Add(delete);
        return root;
    }
}
