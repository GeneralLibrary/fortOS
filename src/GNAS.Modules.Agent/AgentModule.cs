using GNAS.Core;
using GNAS.Modules.Host;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace GNAS.Modules.Agent;

/// <summary>Agent 编排模块，衔接令牌、模板、Compose 与服务监管。</summary>
public sealed class AgentModule : NasModuleBase
{
    /// <inheritdoc />
    public override string ModuleId => "agent";

    /// <inheritdoc />
    public override string DisplayName => "Agent 编排";

    /// <inheritdoc />
    public override IReadOnlyList<string> Dependencies => ["storage"];

    /// <inheritdoc />
    public override IReadOnlyList<string> RequiredCapabilities => ["agent:deploy", "agent:control", "service:write"];

    /// <summary>部署 Agent。</summary>
    public async Task<ServiceDefinition> DeployAgentAsync(string templateId, AgentConfig config, string ownerToken, CancellationToken ct)
    {
        var catalog = RequiredService<IAgentCatalog>();
        var template = await catalog.GetTemplateAsync(templateId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Agent 模板不存在: {templateId}");
        var normalizedConfig = NormalizeConfig(config, Services.GetService(typeof(IGnasConfiguration)) as IGnasConfiguration);
        await RunPreflightChecksAsync(templateId, normalizedConfig, ct).ConfigureAwait(false);
        var compose = await RequiredService<IComposeGenerator>().GenerateAsync(template, normalizedConfig, ownerToken, ct).ConfigureAwait(false);
        var service = new ServiceDefinition
        {
            ServiceId = $"agent-{normalizedConfig.AgentId}",
            DisplayName = normalizedConfig.DisplayName,
            Type = ServiceType.Container,
            ComposeFile = compose.ComposeFilePath,
            RequiredCapabilities = normalizedConfig.Capabilities,
            Startup = ServiceStartup.Manual,
            RestartPolicy = RestartPolicy.OnFailure,
            Quota = normalizedConfig.ResourceQuota
        };
        await RequiredService<IServiceRegistry>().RegisterAsync(service, ct).ConfigureAwait(false);
        await RequiredService<IServiceSupervisor>().StartAsync(service.ServiceId, ct).ConfigureAwait(false);
        await PublishAsync($"agent.{normalizedConfig.AgentId}.deployed", "agent.deployed", new
        {
            normalizedConfig.AgentId,
            templateId,
            service.ServiceId,
            Volumes = normalizedConfig.VolumeMapping.Select(v => new { v.HostPath, v.ContainerPath, v.ReadOnly }).ToArray(),
        }, ct).ConfigureAwait(false);
        return service;
    }

    /// <summary>启动 Agent。</summary>
    public Task StartAgentAsync(string agentId, CancellationToken ct) => RequiredService<IServiceSupervisor>().StartAsync(ServiceId(agentId), ct);

    /// <summary>停止 Agent。</summary>
    public Task StopAgentAsync(string agentId, CancellationToken ct) => RequiredService<IServiceSupervisor>().StopAsync(ServiceId(agentId), ct);

    /// <summary>移除 Agent。</summary>
    public async Task RemoveAgentAsync(string agentId, CancellationToken ct)
    {
        var serviceId = ServiceId(agentId);
        var supervisor = RequiredService<IServiceSupervisor>();
        var registry = RequiredService<IServiceRegistry>();
        try
        {
            await supervisor.StopAsync(serviceId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "停止 Agent {AgentId} 时发生错误，继续注销。", agentId);
        }

        await registry.UnregisterAsync(serviceId, ct).ConfigureAwait(false);
        await RequiredService<ITokenBroker>().RevokeAgentTokenAsync(agentId, "agent removed", ct).ConfigureAwait(false);
        var dir = Path.Combine(Context.DataDirectory, agentId);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }

        await PublishAsync($"agent.{agentId}.removed", "agent.removed", new { agentId }, ct).ConfigureAwait(false);
    }

    /// <summary>列出 Agent 服务。</summary>
    public async Task<IReadOnlyList<ServiceDefinition>> ListAgentsAsync(CancellationToken ct)
    {
        var services = await RequiredService<IServiceRegistry>().ListAsync(ct).ConfigureAwait(false);
        return services.Where(s => s.ServiceId.StartsWith("agent-", StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private static string ServiceId(string agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        return agentId.StartsWith("agent-", StringComparison.OrdinalIgnoreCase) ? agentId : $"agent-{agentId}";
    }

    /// <summary>
    /// 归一化 Agent 配置并补齐默认数据卷。
    /// </summary>
    private static AgentConfig NormalizeConfig(AgentConfig config, IGnasConfiguration? configuration)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(config.AgentId))
        {
            throw new ArgumentException("AgentId 不能为空。", nameof(config));
        }

        if (string.IsNullOrWhiteSpace(config.ImageName))
        {
            throw new ArgumentException("ImageName 不能为空。", nameof(config));
        }

        ValidateImage(config.ImageName, configuration);
        var allowedRoots = ResolveAllowedRoots(configuration);
        var mappings = config.VolumeMapping.Length == 0
            ? [new VolumeMapping
                {
                    HostPath = Path.Combine(GetDataRoot(), "agents-data", config.AgentId),
                    ContainerPath = "/data",
                    ReadOnly = false,
                }]
            : config.VolumeMapping;

        var normalized = mappings.Select(m => NormalizeVolumeMapping(m, allowedRoots)).ToArray();
        return config with { VolumeMapping = normalized };
    }

    private async Task RunPreflightChecksAsync(string templateId, AgentConfig config, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        var processManager = RequiredService<IProcessManager>();

        await EnsureDockerAvailableAsync(processManager, ct).ConfigureAwait(false);
        await EnsureVolumePathsWritableAsync(config.VolumeMapping, ct).ConfigureAwait(false);
        await EnsureImagePullableAsync(processManager, config.ImageName, ct).ConfigureAwait(false);
    }

    private static async Task EnsureDockerAvailableAsync(IProcessManager processManager, CancellationToken ct)
    {
        var result = await processManager.ExecuteCommandAsync(new ProcessStartConfig
        {
            ExecutablePath = "docker",
            Arguments = "version --format \"{{.Server.Version}}\"",
            TimeoutSeconds = 20,
        }, ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException("Docker 不可用，无法部署 Agent。请确认 docker engine 与 socket 可访问。");
        }
    }

    private static async Task EnsureImagePullableAsync(IProcessManager processManager, string imageName, CancellationToken ct)
    {
        var pull = await processManager.ExecuteCommandAsync(new ProcessStartConfig
        {
            ExecutablePath = "docker",
            Arguments = $"pull {Quote(imageName)}",
            TimeoutSeconds = 600,
        }, ct).ConfigureAwait(false);
        if (pull.ExitCode != 0)
        {
            throw new InvalidOperationException($"无法拉取 Agent 镜像 {imageName}：{pull.Stderr}");
        }
    }

    private static async Task EnsureVolumePathsWritableAsync(IEnumerable<VolumeMapping> mappings, CancellationToken ct)
    {
        foreach (var mapping in mappings)
        {
            Directory.CreateDirectory(mapping.HostPath);
            var probe = Path.Combine(mapping.HostPath, ".gnas-write-probe-" + Guid.CreateVersion7().ToString("N"));
            await File.WriteAllTextAsync(probe, "probe", ct).ConfigureAwait(false);
            File.Delete(probe);
        }
    }

    private static VolumeMapping NormalizeVolumeMapping(VolumeMapping mapping, IReadOnlyList<string> allowedRoots)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        if (string.IsNullOrWhiteSpace(mapping.HostPath))
        {
            throw new ArgumentException("卷映射宿主机路径不能为空。", nameof(mapping));
        }

        if (string.IsNullOrWhiteSpace(mapping.ContainerPath))
        {
            throw new ArgumentException("卷映射容器路径不能为空。", nameof(mapping));
        }

        if (mapping.HostPath.Contains('\n') || mapping.HostPath.Contains('\r')
            || mapping.ContainerPath.Contains('\n') || mapping.ContainerPath.Contains('\r'))
        {
            throw new ArgumentException("卷映射路径不能包含换行。", nameof(mapping));
        }

        if (!IsAbsolutePath(mapping.HostPath))
        {
            throw new ArgumentException("卷映射宿主机路径必须是绝对路径。", nameof(mapping));
        }

        if (!mapping.ContainerPath.StartsWith("/", StringComparison.Ordinal))
        {
            throw new ArgumentException("卷映射容器路径必须是 Unix 绝对路径。", nameof(mapping));
        }

        var normalizedHostPath = NormalizePath(mapping.HostPath);
        if (!allowedRoots.Any(root => IsPathUnderRoot(normalizedHostPath, root)))
        {
            throw new ArgumentException($"卷映射路径 {mapping.HostPath} 不在允许目录内。", nameof(mapping));
        }

        return mapping with { HostPath = mapping.HostPath };
    }

    private static void ValidateImage(string imageName, IGnasConfiguration? configuration)
    {
        if (imageName.IndexOfAny(['\r', '\n', ' ', ';', '&', '|', '`', '$']) >= 0)
            throw new ArgumentException("Agent image name contains unsafe characters.", nameof(imageName));
        var allowed = configuration?.GetArray("agent:allowed_images") ?? [];
        if (allowed.Length > 0 && !allowed.Any(prefix => imageName.StartsWith(prefix.TrimEnd('*'), StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Agent image is not in the configured allowlist.", nameof(imageName));
        var production = string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Production", StringComparison.OrdinalIgnoreCase);
        if (production && !imageName.Contains("@sha256:", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Production Agent images must be pinned by sha256 digest.", nameof(imageName));
    }

    private static string[] ResolveAllowedRoots(IGnasConfiguration? configuration)
    {
        var configured = configuration?.GetArray("agent:allowed_volume_roots") ?? [];
        if (configured.Length == 0 && configuration?.GetValue("agent:allowed_volume_roots") is { Length: > 0 } value)
        {
            configured = value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        var roots = configured.Length == 0
            ? [GetDataRoot()]
            : configured;

        return roots.Select(NormalizePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string GetDataRoot()
    {
        var root = Environment.GetEnvironmentVariable("GNAS_DATA_ROOT");
        return string.IsNullOrWhiteSpace(root) ? "/srv/nas" : root;
    }

    private static bool IsAbsolutePath(string path)
        => Path.IsPathFullyQualified(path) || path.StartsWith("/", StringComparison.Ordinal);

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("路径不能为空。", nameof(path));
        }

        var unix = path.Replace('\\', '/');
        if (unix.StartsWith("/", StringComparison.Ordinal))
        {
            return TrimTrailingSlash(Regex.Replace(unix, "/{2,}", "/"));
        }

        return TrimTrailingSlash(Path.GetFullPath(path).Replace('\\', '/'));
    }

    private static bool IsPathUnderRoot(string path, string root)
        => string.Equals(path, root, StringComparison.OrdinalIgnoreCase)
           || path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);

    private static string TrimTrailingSlash(string path)
        => path.Length > 1 ? path.TrimEnd('/') : path;

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
