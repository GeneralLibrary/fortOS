using System.CommandLine;
using System.Text.Json;
using GORT.Cli.ApiClient;
using Spectre.Console;

namespace GORT.Cli.Commands;

/// <summary>Register audit, alert, auth, config, UPS and recovery commands.</summary>
public static class MiscCommands
{
    /// <summary>Create audit command.</summary>
    public static Command Audit(CliOptions options)
    {
        var root = new Command("audit", "Audit tools"); var verify = new Command("verify", "Verify audit chain");
        verify.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.GetAsync("api/audit/verify", t), cancellationToken: ct));
        root.Add(verify); return root;
    }

    /// <summary>Create alert command.</summary>
    public static Command Alert(CliOptions options)
    {
        var root = new Command("alert", "Alert management");
        var list = new Command("list", "List alerts"); list.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.GetAsync("api/alerts", t), cancellationToken: ct));
        var rules = new Command("rules", "List alert rules"); rules.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.GetAsync("api/alerts/rules", t), cancellationToken: ct));
        root.Add(list); root.Add(rules); return root;
    }

    /// <summary>Create auth command.</summary>
    public static Command Auth(CliOptions options)
    {
        var root = new Command("auth", "Authentication");
        var username = new Option<string?>("--username") { Description = "Username" };
        var login = new Command("login", "Login and save token") { username };
        login.SetAction(async (p, ct) =>
        {
            CommandRuntime.ApplyConsole(p, options);
            var user = p.GetValue(username) ?? AnsiConsole.Ask<string>("Username：");
            var password = AnsiConsole.Prompt(new TextPrompt<string>("Password:").Secret());
            try
            {
                using var client = CommandRuntime.Client(p, options);
                using var doc = await client.PostAsync("api/auth/login", new { username = user, password }, ct);
                var token = FindString(doc.RootElement, "token") ?? FindString(doc.RootElement, "accessToken") ?? FindString(doc.RootElement, "jwt");
                AuthStore.Save(client.Server, token);
                if (CommandRuntime.IsJson(p, options)) CommandRuntime.PrintJson(doc); else AnsiConsole.MarkupLine("[green]Login successful, token saved.[/]");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
        });
        var whoami = new Command("whoami", "Show current authentication info");
        whoami.SetAction(p =>
        {
            var store = AuthStore.Load();
            if (CommandRuntime.IsJson(p, options)) Console.WriteLine(JsonSerializer.Serialize(new { store.Server, hasToken = !string.IsNullOrWhiteSpace(store.Token) }, ApiJson.Options));
            else CommandRuntime.RenderKeyValues("Authentication", ("Server", store.Server ?? "(not set)"), ("Token", string.IsNullOrWhiteSpace(store.Token) ? "Not saved" : "Saved"));
            return 0;
        });
        root.Add(login); root.Add(whoami); return root;
    }

    /// <summary>Create config command.</summary>
    public static Command Config(CliOptions options)
    {
        var root = new Command("config", "Configuration management"); var key = new Argument<string>("key");
        var get = new Command("get", "Read configuration") { key };
        get.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.GetAsync("api/config?key=" + Uri.EscapeDataString(p.GetRequiredValue(key)), t), cancellationToken: ct));
        var key2 = new Argument<string>("key"); var value = new Argument<string>("value");
        var set = new Command("set", "Set configuration") { key2, value };
        set.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.PutAsync("api/config/" + Uri.EscapeDataString(p.GetRequiredValue(key2)), new { value = p.GetRequiredValue(value) }, t), cancellationToken: ct));
        root.Add(get); root.Add(set); return root;
    }

    /// <summary>Create UPS command.</summary>
    public static Command Ups(CliOptions options)
    {
        var root = new Command("ups", "UPS status"); var status = new Command("status", "Show UPS status");
        status.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.GetAsync("api/ups/status", t), cancellationToken: ct));
        root.Add(status); return root;
    }

    /// <summary>Create recovery command.</summary>
    public static Command Recovery(CliOptions options)
    {
        var root = new Command("recovery", "Recovery process");
        var target = new Argument<string>("target") { Description = "Recovery target path" };
        var source = new Option<string?>("--source") { Description = "Data source path for rsync mode" };
        var snapshotId = new Option<string?>("--snapshot-id") { Description = "Snapshot ID/path for snapshot mode" };
        var mode = new Option<string?>("--mode") { Description = "Recovery mode: rsync or snapshot (auto-detected by default)" };
        var dryRun = new Option<bool>("--dry-run") { Description = "Dry run only (rsync mode only)" };
        var confirm = new Option<bool>("--confirm");
        var start = new Command("start", "Start recovery") { target, source, snapshotId, mode, dryRun, confirm };
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
