using System.CommandLine;
using System.Text.Json;
using GNAS.Cli.ApiClient;
using Spectre.Console;

namespace GNAS.Cli.Commands;

/// <summary>注册审计、警报、认证、配置、电源与恢复命令。</summary>
public static class MiscCommands
{
    /// <summary>建立 audit 命令。</summary>
    public static Command Audit(CliOptions options)
    {
        var root = new Command("audit", "审计工具"); var verify = new Command("verify", "验证审计链");
        verify.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.GetAsync("api/audit/verify", t), cancellationToken: ct));
        root.Add(verify); return root;
    }

    /// <summary>建立 alert 命令。</summary>
    public static Command Alert(CliOptions options)
    {
        var root = new Command("alert", "警报管理");
        var list = new Command("list", "列出警报"); list.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.GetAsync("api/alerts", t), cancellationToken: ct));
        var rules = new Command("rules", "列出警报规则"); rules.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.GetAsync("api/alerts/rules", t), cancellationToken: ct));
        root.Add(list); root.Add(rules); return root;
    }

    /// <summary>建立 auth 命令。</summary>
    public static Command Auth(CliOptions options)
    {
        var root = new Command("auth", "认证");
        var username = new Option<string?>("--username") { Description = "用户名" };
        var login = new Command("login", "登入并保存令牌") { username };
        login.SetAction(async (p, ct) =>
        {
            CommandRuntime.ApplyConsole(p, options);
            var user = p.GetValue(username) ?? AnsiConsole.Ask<string>("用户名：");
            var password = AnsiConsole.Prompt(new TextPrompt<string>("密码：").Secret());
            try
            {
                using var client = CommandRuntime.Client(p, options);
                using var doc = await client.PostAsync("api/auth/login", new { username = user, password }, ct);
                var token = FindString(doc.RootElement, "token") ?? FindString(doc.RootElement, "accessToken") ?? FindString(doc.RootElement, "jwt");
                AuthStore.Save(client.Server, token);
                if (CommandRuntime.IsJson(p, options)) CommandRuntime.PrintJson(doc); else AnsiConsole.MarkupLine("[green]登入成功，令牌已保存。[/]");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
        });
        var whoami = new Command("whoami", "显示当前认证资料");
        whoami.SetAction(p =>
        {
            var store = AuthStore.Load();
            if (CommandRuntime.IsJson(p, options)) Console.WriteLine(JsonSerializer.Serialize(new { store.Server, hasToken = !string.IsNullOrWhiteSpace(store.Token) }, ApiJson.Options));
            else CommandRuntime.RenderKeyValues("认证", ("服务器", store.Server ?? "(未设置)"), ("令牌", string.IsNullOrWhiteSpace(store.Token) ? "未保存" : "已保存"));
            return 0;
        });
        root.Add(login); root.Add(whoami); return root;
    }

    /// <summary>建立 config 命令。</summary>
    public static Command Config(CliOptions options)
    {
        var root = new Command("config", "配置管理"); var key = new Argument<string>("key");
        var get = new Command("get", "读取配置") { key };
        get.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.GetAsync("api/config?key=" + Uri.EscapeDataString(p.GetRequiredValue(key)), t), cancellationToken: ct));
        var key2 = new Argument<string>("key"); var value = new Argument<string>("value");
        var set = new Command("set", "设置配置") { key2, value };
        set.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.PutAsync("api/config/" + Uri.EscapeDataString(p.GetRequiredValue(key2)), new { value = p.GetRequiredValue(value) }, t), cancellationToken: ct));
        root.Add(get); root.Add(set); return root;
    }

    /// <summary>建立 ups 命令。</summary>
    public static Command Ups(CliOptions options)
    {
        var root = new Command("ups", "UPS 状态"); var status = new Command("status", "显示 UPS 状态");
        status.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.GetAsync("api/ups/status", t), cancellationToken: ct));
        root.Add(status); return root;
    }

    /// <summary>建立 recovery 命令。</summary>
    public static Command Recovery(CliOptions options)
    {
        var root = new Command("recovery", "恢复流程");
        var target = new Argument<string>("target") { Description = "恢复目标路径" };
        var source = new Option<string?>("--source") { Description = "rsync 模式的数据来源路径" };
        var snapshotId = new Option<string?>("--snapshot-id") { Description = "snapshot 模式的快照标识/路径" };
        var mode = new Option<string?>("--mode") { Description = "恢复模式：rsync 或 snapshot（默认自动推断）" };
        var dryRun = new Option<bool>("--dry-run") { Description = "仅演练（仅 rsync 模式）" };
        var confirm = new Option<bool>("--confirm");
        var start = new Command("start", "启动恢复") { target, source, snapshotId, mode, dryRun, confirm };
        start.SetAction((p, ct) => CommandRuntime.RequireConfirm(p.GetValue(confirm))
            ? CommandRuntime.RunAsync(p, options, (c, t) => c.PostAsync("api/recovery/start", new
            {
                target = p.GetRequiredValue(target),
                source = p.GetValue(source),
                snapshotId = p.GetValue(snapshotId),
                mode = p.GetValue(mode),
                dryRun = p.GetValue(dryRun),
            }, t), cancellationToken: ct)
            : Task.FromResult(2));
        root.Add(start); return root;
    }

    private static string? FindString(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in element.EnumerateObject())
            {
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String) return p.Value.GetString();
                var nested = FindString(p.Value, name); if (nested is not null) return nested;
            }
        }
        return null;
    }
}
