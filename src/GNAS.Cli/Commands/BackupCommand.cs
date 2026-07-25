using System.CommandLine;

namespace GNAS.Cli.Commands;

/// <summary>注册备份任务命令。</summary>
public static class BackupCommand
{
    /// <summary>建立 backup 命令。</summary>
    public static Command Create(CliOptions options)
    {
        var root = new Command("backup", "备份任务管理");

        var task = new Command("task", "任务管理");
        task.Add(BuildTaskList(options));
        task.Add(BuildTaskSet(options));
        task.Add(BuildTaskDelete(options));
        task.Add(BuildTaskRun(options));
        task.Add(BuildTaskRestore(options));

        var run = new Command("run", "运行历史");
        run.Add(BuildRunList(options));

        root.Add(task);
        root.Add(run);
        return root;
    }

    private static Command BuildTaskList(CliOptions options)
    {
        var command = new Command("list", "列出备份任务");
        command.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.GetAsync("api/backup/tasks", t), cancellationToken: ct));
        return command;
    }

    private static Command BuildTaskSet(CliOptions options)
    {
        var taskId = new Argument<string>("task-id");
        var name = new Option<string>("--name");
        var source = new Option<string>("--source");
        var target = new Option<string>("--target");
        var cron = new Option<string>("--cron") { Description = "daily HH:mm 或 interval:N" };
        var targetType = new Option<string>("--target-type") { Description = "local/remoteNas/s3/b2/webdav/sftp", DefaultValueFactory = _ => "local" };
        var connection = new Option<string?>("--connection") { Description = "目标连接字符串（默认等于 --target）" };
        var method = new Option<string>("--method") { Description = "incremental/full/mirror", DefaultValueFactory = _ => "incremental" };
        var disabled = new Option<bool>("--disabled");
        var command = new Command("set", "创建或更新任务")
        {
            taskId, name, source, target, cron, targetType, connection, method, disabled
        };
        command.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) =>
        {
            var id = p.GetRequiredValue(taskId);
            var targetPath = p.GetValue(target) ?? throw new ArgumentException("缺少 --target");
            var sourcePath = p.GetValue(source) ?? throw new ArgumentException("缺少 --source");
            var taskName = p.GetValue(name) ?? id;
            var schedule = p.GetValue(cron) ?? "interval:60";
            var connectionString = p.GetValue(connection) ?? targetPath;
            return c.PutAsync($"api/backup/tasks/{Uri.EscapeDataString(id)}", new
            {
                taskId = id,
                name = taskName,
                sourcePath,
                cronExpression = schedule,
                enabled = !p.GetValue(disabled),
                method = ToEnumName(p.GetValue(method)),
                target = new
                {
                    type = ToEnumName(p.GetValue(targetType)),
                    connectionString,
                    bucketOrPath = targetPath,
                },
                retentionDays = 30,
                retentionCount = 10,
                compression = true,
                encryption = false,
                excludePatterns = Array.Empty<string>(),
            }, t);
        }, cancellationToken: ct));
        return command;
    }

    private static Command BuildTaskDelete(CliOptions options)
    {
        var taskId = new Argument<string>("task-id");
        var confirm = new Option<bool>("--confirm");
        var command = new Command("delete", "删除任务") { taskId, confirm };
        command.SetAction((p, ct) => CommandRuntime.RequireConfirm(p.GetValue(confirm))
            ? CommandRuntime.RunAsync(p, options, (c, t) => c.DeleteAsync($"api/backup/tasks/{Uri.EscapeDataString(p.GetRequiredValue(taskId))}", t), cancellationToken: ct)
            : Task.FromResult(2));
        return command;
    }

    private static Command BuildTaskRun(CliOptions options)
    {
        var taskId = new Argument<string>("task-id");
        var command = new Command("run", "立即执行任务") { taskId };
        command.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.PostAsync($"api/backup/tasks/{Uri.EscapeDataString(p.GetRequiredValue(taskId))}/run", null, t), cancellationToken: ct));
        return command;
    }

    private static Command BuildTaskRestore(CliOptions options)
    {
        var taskId = new Argument<string>("task-id");
        var source = new Option<string?>("--source");
        var target = new Option<string?>("--target");
        var dryRun = new Option<bool>("--dry-run");
        var command = new Command("restore", "从备份恢复任务数据") { taskId, source, target, dryRun };
        command.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.PostAsync($"api/backup/tasks/{Uri.EscapeDataString(p.GetRequiredValue(taskId))}/restore", new
        {
            sourceOverride = p.GetValue(source),
            targetOverride = p.GetValue(target),
            dryRun = p.GetValue(dryRun),
        }, t), cancellationToken: ct));
        return command;
    }

    private static Command BuildRunList(CliOptions options)
    {
        var taskId = new Option<string?>("--task-id");
        var limit = new Option<int>("--limit") { DefaultValueFactory = _ => 100 };
        var command = new Command("list", "列出运行历史") { taskId, limit };
        command.SetAction((p, ct) =>
        {
            var query = $"?limit={Math.Clamp(p.GetValue(limit), 1, 1000)}";
            if (!string.IsNullOrWhiteSpace(p.GetValue(taskId)))
            {
                query += "&taskId=" + Uri.EscapeDataString(p.GetValue(taskId)!);
            }

            return CommandRuntime.RunAsync(p, options, (c, t) => c.GetAsync("api/backup/runs" + query, t), cancellationToken: ct);
        });
        return command;
    }

    private static string ToEnumName(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("枚举值不能为空。", nameof(value));
        }

        return char.ToUpperInvariant(normalized[0]) + normalized[1..];
    }
}
