using System.CommandLine;

namespace GORT.Cli.Commands;

/// <summary>Register file management commands.</summary>
public static class FileCommand
{
    /// <summary>Create file command.</summary>
    public static Command Create(CliOptions options)
    {
        var root = new Command("file", "File management");

        var listPath = new Argument<string>("path");
        var recursive = new Option<bool>("--recursive");
        var list = new Command("list", "List directory") { listPath, recursive };
        list.SetAction((p, ct) => CommandRuntime.RunAsync(p, options,
            (c, t) => c.GetAsync($"api/files?path={Uri.EscapeDataString(p.GetRequiredValue(listPath))}&recursive={p.GetValue(recursive).ToString().ToLowerInvariant()}", t),
            cancellationToken: ct));

        var statPath = new Argument<string>("path");
        var stat = new Command("stat", "View path metadata") { statPath };
        stat.SetAction((p, ct) => CommandRuntime.RunAsync(p, options,
            (c, t) => c.GetAsync($"api/files/stat?path={Uri.EscapeDataString(p.GetRequiredValue(statPath))}", t),
            cancellationToken: ct));

        var readPath = new Argument<string>("path");
        var asBase64 = new Option<bool>("--base64");
        var read = new Command("read", "Read file") { readPath, asBase64 };
        read.SetAction((p, ct) => CommandRuntime.RunAsync(p, options,
            (c, t) => c.GetAsync($"api/files/content?path={Uri.EscapeDataString(p.GetRequiredValue(readPath))}&encoding={(p.GetValue(asBase64) ? "base64" : "text")}", t),
            cancellationToken: ct));

        var writePath = new Argument<string>("path");
        var writeContent = new Option<string>("--content") { Description = "Content to write" };
        var writeBase64 = new Option<bool>("--base64");
        var writeOverwrite = new Option<bool>("--overwrite");
        var write = new Command("write", "Create or write file") { writePath, writeContent, writeBase64, writeOverwrite };
        write.SetAction((p, ct) => CommandRuntime.RunAsync(p, options,
            (c, t) => c.PostAsync("api/files/write", new
            {
                path = p.GetRequiredValue(writePath),
                content = p.GetValue(writeContent) ?? string.Empty,
                encoding = p.GetValue(writeBase64) ? "base64" : "text",
                overwrite = p.GetValue(writeOverwrite)
            }, t),
            cancellationToken: ct));

        var updatePath = new Argument<string>("path");
        var updateContent = new Option<string>("--content") { Description = "Content to update" };
        var updateBase64 = new Option<bool>("--base64");
        var update = new Command("update", "Update file") { updatePath, updateContent, updateBase64 };
        update.SetAction((p, ct) => CommandRuntime.RunAsync(p, options,
            (c, t) => c.PutAsync("api/files/content", new
            {
                path = p.GetRequiredValue(updatePath),
                content = p.GetValue(updateContent) ?? string.Empty,
                encoding = p.GetValue(updateBase64) ? "base64" : "text",
            }, t),
            cancellationToken: ct));

        var mkdirPath = new Argument<string>("path");
        var mkdir = new Command("mkdir", "Create directory") { mkdirPath };
        mkdir.SetAction((p, ct) => CommandRuntime.RunAsync(p, options,
            (c, t) => c.PostAsync("api/files/mkdir", new { path = p.GetRequiredValue(mkdirPath) }, t),
            cancellationToken: ct));

        var moveSource = new Argument<string>("source");
        var moveDest = new Argument<string>("destination");
        var moveOverwrite = new Option<bool>("--overwrite");
        var move = new Command("move", "Move path") { moveSource, moveDest, moveOverwrite };
        move.SetAction((p, ct) => CommandRuntime.RunAsync(p, options,
            (c, t) => c.PostAsync("api/files/move", new
            {
                sourcePath = p.GetRequiredValue(moveSource),
                destinationPath = p.GetRequiredValue(moveDest),
                overwrite = p.GetValue(moveOverwrite)
            }, t),
            cancellationToken: ct));

        var copySource = new Argument<string>("source");
        var copyDest = new Argument<string>("destination");
        var copyOverwrite = new Option<bool>("--overwrite");
        var copy = new Command("copy", "Copy path") { copySource, copyDest, copyOverwrite };
        copy.SetAction((p, ct) => CommandRuntime.RunAsync(p, options,
            (c, t) => c.PostAsync("api/files/copy", new
            {
                sourcePath = p.GetRequiredValue(copySource),
                destinationPath = p.GetRequiredValue(copyDest),
                overwrite = p.GetValue(copyOverwrite)
            }, t),
            cancellationToken: ct));

        var deletePath = new Argument<string>("path");
        var hard = new Option<bool>("--hard");
        var confirm = new Option<bool>("--confirm");
        var delete = new Command("delete", "Delete path (soft delete by default)") { deletePath, hard, confirm };
        delete.SetAction((p, ct) => CommandRuntime.RequireConfirm(p.GetValue(confirm))
            ? CommandRuntime.RunAsync(p, options,
                (c, t) => c.DeleteAsync($"api/files?path={Uri.EscapeDataString(p.GetRequiredValue(deletePath))}&hard={p.GetValue(hard).ToString().ToLowerInvariant()}", t),
                cancellationToken: ct)
            : Task.FromResult(2));

        var restoreRecycle = new Argument<string>("recycle-path");
        var restoreTarget = new Argument<string>("target-path");
        var restore = new Command("restore", "Restore from recycle bin") { restoreRecycle, restoreTarget };
        restore.SetAction((p, ct) => CommandRuntime.RunAsync(p, options,
            (c, t) => c.PostAsync("api/files/restore", new
            {
                recyclePath = p.GetRequiredValue(restoreRecycle),
                targetPath = p.GetRequiredValue(restoreTarget),
            }, t),
            cancellationToken: ct));

        var chmodPath = new Argument<string>("path");
        var mode = new Argument<string>("mode");
        var chmod = new Command("chmod", "Modify Linux permission bits") { chmodPath, mode };
        chmod.SetAction((p, ct) => CommandRuntime.RunAsync(p, options,
            (c, t) => c.PostAsync("api/files/chmod", new { path = p.GetRequiredValue(chmodPath), mode = p.GetRequiredValue(mode) }, t),
            cancellationToken: ct));

        var chownPath = new Argument<string>("path");
        var owner = new Argument<string>("owner");
        var chown = new Command("chown", "Modify Linux owner") { chownPath, owner };
        chown.SetAction((p, ct) => CommandRuntime.RunAsync(p, options,
            (c, t) => c.PostAsync("api/files/chown", new { path = p.GetRequiredValue(chownPath), owner = p.GetRequiredValue(owner) }, t),
            cancellationToken: ct));

        root.Add(list);
        root.Add(stat);
        root.Add(read);
        root.Add(write);
        root.Add(update);
        root.Add(mkdir);
        root.Add(move);
        root.Add(copy);
        root.Add(delete);
        root.Add(restore);
        root.Add(chmod);
        root.Add(chown);
        return root;
    }
}
