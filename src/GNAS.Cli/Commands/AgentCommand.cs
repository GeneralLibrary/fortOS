using System.CommandLine;
using System.Globalization;

namespace GNAS.Cli.Commands;

/// <summary>注册代理命令。</summary>
public static class AgentCommand
{
    /// <summary>建立 agent 命令。</summary>
    public static Command Create(CliOptions options)
    {
        var root = new Command("agent", "应用代理管理");
        var list = new Command("list", "列出代理");
        list.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.GetAsync("api/agents", t), cancellationToken: ct));
        var catalog = BuildCatalogCommand(options);
        var deploy = BuildDeployCommand(options);
        root.Add(list); root.Add(deploy); root.Add(catalog);
        foreach (var verb in new[] { "start", "stop" })
        {
            var id = new Argument<string>("id"); var cmd = new Command(verb, $"{verb} 代理") { id };
            cmd.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.PostAsync($"api/agents/{Uri.EscapeDataString(p.GetRequiredValue(id))}/{verb}", null, t), cancellationToken: ct));
            root.Add(cmd);
        }
        var logId = new Argument<string>("id"); var follow = new Option<bool>("--follow");
        var logs = new Command("logs", "查看代理日志") { logId, follow };
        logs.SetAction(async (p, ct) =>
        {
            if (!p.GetValue(follow)) return await CommandRuntime.RunAsync(p, options, (c, t) => c.GetAsync($"api/agents/{Uri.EscapeDataString(p.GetRequiredValue(logId))}/logs", t), cancellationToken: ct);
            try { using var c = CommandRuntime.Client(p, options); await foreach (var line in c.GetSseStreamAsync($"api/agents/{Uri.EscapeDataString(p.GetRequiredValue(logId))}/logs", ct)) Console.WriteLine(line); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
        });
        var removeId = new Argument<string>("id"); var confirm = new Option<bool>("--confirm");
        var remove = new Command("remove", "移除代理") { removeId, confirm };
        remove.SetAction((p, ct) => CommandRuntime.RequireConfirm(p.GetValue(confirm)) ? CommandRuntime.RunAsync(p, options, (c, t) => c.DeleteAsync($"api/agents/{Uri.EscapeDataString(p.GetRequiredValue(removeId))}", t), cancellationToken: ct) : Task.FromResult(2));
        root.Add(logs); root.Add(remove);
        return root;
    }

    private static Command BuildCatalogCommand(CliOptions options)
    {
        var catalog = new Command("catalog", "模板目录管理");
        catalog.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.GetAsync("api/agents/catalog", t), cancellationToken: ct));

        var list = new Command("list", "列出模板");
        list.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.GetAsync("api/agents/catalog", t), cancellationToken: ct));

        var searchQuery = new Argument<string>("query");
        var search = new Command("search", "搜索模板") { searchQuery };
        search.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.GetAsync("api/agents/catalog/search?query=" + Uri.EscapeDataString(p.GetRequiredValue(searchQuery)), t), cancellationToken: ct));

        var installSource = new Argument<string>("source");
        var install = new Command("install", "安装模板（本地路径或 URL）") { installSource };
        install.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.PostAsync("api/agents/catalog/install", new { source = p.GetRequiredValue(installSource) }, t), cancellationToken: ct));

        var updateTemplate = new Argument<string>("template");
        var update = new Command("update", "更新模板") { updateTemplate };
        update.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) => c.PostAsync($"api/agents/catalog/{Uri.EscapeDataString(p.GetRequiredValue(updateTemplate))}/update", null, t), cancellationToken: ct));

        catalog.Add(list);
        catalog.Add(search);
        catalog.Add(install);
        catalog.Add(update);
        return catalog;
    }

    private static Command BuildDeployCommand(CliOptions options)
    {
        var template = new Argument<string>("template");
        var agentId = new Option<string>("--agent-id") { Description = "Agent 标识（默认自动生成）" };
        var displayName = new Option<string?>("--display-name") { Description = "显示名称（默认同 agent-id）" };
        var image = new Option<string>("--image") { Description = "容器镜像，例如 nginx:alpine" };
        var cap = new Option<string[]>("--cap") { Arity = ArgumentArity.ZeroOrMore, Description = "授权能力，可重复" };
        var volume = new Option<string[]>("--volume") { Arity = ArgumentArity.ZeroOrMore, Description = "卷映射 host:container[:ro|rw]，可重复" };
        var port = new Option<string[]>("--port") { Arity = ArgumentArity.ZeroOrMore, Description = "端口映射 host:container[/tcp|udp]，可重复" };
        var cpu = new Option<double?>("--cpu") { Description = "CPU 上限，例如 1.5" };
        var memoryMiB = new Option<long?>("--memory-mib") { Description = "内存上限（MiB）" };
        var param = new Option<string[]>("--param") { Arity = ArgumentArity.ZeroOrMore, AllowMultipleArgumentsPerToken = false, Description = "兼容参数 k=v，可重复" };
        var deploy = new Command("deploy", "部署代理")
        {
            template,
            agentId,
            displayName,
            image,
            cap,
            volume,
            port,
            cpu,
            memoryMiB,
            param
        };
        deploy.SetAction((p, ct) => CommandRuntime.RunAsync(p, options, (c, t) =>
        {
            var pairs = CommandRuntime.ParsePairs(p.GetValue(param));
            var resolvedTemplate = p.GetRequiredValue(template);
            var resolvedAgentId = p.GetValue(agentId)
                ?? Read(pairs, "agentId", "agent-id", "id")
                ?? "agent-" + Guid.CreateVersion7().ToString("N")[..8];
            var resolvedImage = p.GetValue(image)
                ?? Read(pairs, "image", "imageName", "image-name")
                ?? throw new ArgumentException("部署代理必须指定 --image 或 --param image=...");
            var resolvedDisplay = p.GetValue(displayName)
                ?? Read(pairs, "displayName", "display-name", "name")
                ?? resolvedAgentId;
            var caps = p.GetValue(cap) is { Length: > 0 } explicitCaps
                ? explicitCaps
                : SplitCsv(Read(pairs, "capabilities", "caps"));
            var volumes = p.GetValue(volume) is { Length: > 0 } explicitVolumes
                ? explicitVolumes.Select(ParseVolume).ToArray()
                : SplitCsv(Read(pairs, "volumes", "volume")).Select(ParseVolume).ToArray();
            var ports = p.GetValue(port) is { Length: > 0 } explicitPorts
                ? explicitPorts.Select(ParsePort).ToArray()
                : SplitCsv(Read(pairs, "ports", "port")).Select(ParsePort).ToArray();
            long? memoryLimitBytes = p.GetValue(memoryMiB) is { } mib ? mib * 1024 * 1024 : null;
            var quota = p.GetValue(cpu) is null && p.GetValue(memoryMiB) is null
                ? null
                : new
                {
                    cpuLimit = p.GetValue(cpu),
                    memoryLimitBytes,
                };
            return c.PostAsync("api/agents/deploy", new
            {
                templateId = resolvedTemplate,
                config = new
                {
                    agentId = resolvedAgentId,
                    displayName = resolvedDisplay,
                    imageName = resolvedImage,
                    capabilities = caps,
                    volumeMapping = volumes,
                    portMapping = ports,
                    resourceQuota = quota,
                }
            }, t);
        }, cancellationToken: ct));
        return deploy;
    }

    private static string? Read(IReadOnlyDictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static string[] SplitCsv(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static object ParseVolume(string value)
    {
        var parts = value.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts.Length > 3)
        {
            throw new ArgumentException("卷映射格式应为 host:container[:ro|rw]。", nameof(value));
        }

        return new
        {
            hostPath = parts[0],
            containerPath = parts[1],
            readOnly = parts.Length == 3 && string.Equals(parts[2], "ro", StringComparison.OrdinalIgnoreCase),
        };
    }

    private static object ParsePort(string value)
    {
        var protocol = "tcp";
        var pair = value;
        var slashIndex = value.IndexOf('/', StringComparison.Ordinal);
        if (slashIndex >= 0)
        {
            protocol = value[(slashIndex + 1)..];
            pair = value[..slashIndex];
        }

        var parts = pair.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hostPort)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var containerPort))
        {
            throw new ArgumentException("端口映射格式应为 host:container[/tcp|udp]。", nameof(value));
        }

        return new
        {
            hostPort,
            containerPort,
            protocol,
        };
    }
}
