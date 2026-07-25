using System.CommandLine;

namespace GNAS.Cli.Commands;

/// <summary>注册文件管理命令。</summary>
public static class FileCommand
{
    /// <summary>建立 file 命令。</summary>
    public static Command Create(CliOptions options)
    {
        var root = new Command("file", "文件管理");

        var listPath = new Argument<string>("path");
        var recursive = new Option<bool>("--recursive");
        var list = new Command("list", "列出目录") { listPath, recursive };
        list.SetAction((p, ct) => CommandRuntime.RunAsync(p, options,
            (c, t) => c.GetAsync($"api/files?path={Uri.EscapeDataString(p.GetRequiredValue(listPath))}&recursive={p.GetValue(recursive).ToString().ToLowerInvariant()}", t),
            cancellationToken: ct));

        var statPath = new Argument<string>("path");
        var stat = new Command("stat", "查看路径元数据") { statPath };
        stat.SetAction((p, ct) => CommandRuntime.RunAsync(p, options,
            (c, t) => c.GetAsync($"api/files/stat?path={Uri.EscapeDataString(p.GetRequiredValue(statPath))}", t),
            cancellationToken: ct));

        var readPath = new Argument<string>("path");
        var asBase64 = new Option<bool>("--base64");
        var read = new Command("read", "读取文件") { readPath, asBase64 };
        read.SetAction((p, ct) => CommandRuntime.RunAsync(p, options,
            (c, t) => c.GetAsync($"api/files/content?path={Uri.EscapeDataString(p.GetRequiredValue(readPath))}&encoding={(p.GetValue(asBase64) ? "base64" : "text")}", t),
            cancellationToken: ct));

        var writePath = new Argument<string>("path");
        var writeContent = new Option<string>("--content") { Description = "写入内容" };
        var writeBase64 = new Option<bool>("--base64");
        var writeOverwrite = new Option<bool>("--overwrite");
        var write = new Command("write", "创建或写入文件") { writePath, writeContent, writeBase64, writeOverwrite };
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
        var updateContent = new Option<string>("--content") { Description = "更新内容" };
        var updateBase64 = new Option<bool>("--base64");
        var update = new Command("update", "更新文件") { updatePath, updateContent, updateBase64 };
        update.SetAction((p, ct) => CommandRuntime.RunAsync(p, options,
            (c, t) => c.PutAsync("api/files/content", new
            {
                path = p.GetRequiredValue(updatePath),
                content = p.GetValue(updateContent) ?? string.Empty,
                encoding = p.GetValue(updateBase64) ? "base64" : "text",
            }, t),
            cancellationToken: ct));

        var mkdirPath = new Argument<string>("path");
        var mkdir = new Command("mkdir", "创建目录") { mkdirPath };
        mkdir.SetAction((p, ct) => CommandRuntime.RunAsync(p, options,
            (c, t) => c.PostAsync("api/files/mkdir", new { path = p.GetRequiredValue(mkdirPath) }, t),
            cancellationToken: ct));

        var moveSource = new Argument<string>("source");
        var moveDest = new Argument<string>("destination");
        var moveOverwrite = new Option<bool>("--overwrite");
        var move = new Command("move", "移动路径") { moveSource, moveDest, moveOverwrite };
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
        var copy = new Command("copy", "复制路径") { copySource, copyDest, copyOverwrite };
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
        var delete = new Command("delete", "删除路径（默认软删除）") { deletePath, hard, confirm };
        delete.SetAction((p, ct) => CommandRuntime.RequireConfirm(p.GetValue(confirm))
            ? CommandRuntime.RunAsync(p, options,
                (c, t) => c.DeleteAsync($"api/files?path={Uri.EscapeDataString(p.GetRequiredValue(deletePath))}&hard={p.GetValue(hard).ToString().ToLowerInvariant()}", t),
                cancellationToken: ct)
            : Task.FromResult(2));

        var restoreRecycle = new Argument<string>("recycle-path");
        var restoreTarget = new Argument<string>("target-path");
        var restore = new Command("restore", "从回收站恢复") { restoreRecycle, restoreTarget };
        restore.SetAction((p, ct) => CommandRuntime.RunAsync(p, options,
            (c, t) => c.PostAsync("api/files/restore", new
            {
                recyclePath = p.GetRequiredValue(restoreRecycle),
                targetPath = p.GetRequiredValue(restoreTarget),
            }, t),
            cancellationToken: ct));

        var chmodPath = new Argument<string>("path");
        var mode = new Argument<string>("mode");
        var chmod = new Command("chmod", "修改 Linux 权限位") { chmodPath, mode };
        chmod.SetAction((p, ct) => CommandRuntime.RunAsync(p, options,
            (c, t) => c.PostAsync("api/files/chmod", new { path = p.GetRequiredValue(chmodPath), mode = p.GetRequiredValue(mode) }, t),
            cancellationToken: ct));

        var chownPath = new Argument<string>("path");
        var owner = new Argument<string>("owner");
        var chown = new Command("chown", "修改 Linux 所有者") { chownPath, owner };
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
