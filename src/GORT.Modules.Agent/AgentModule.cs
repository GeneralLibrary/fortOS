using GORT.Core;
using GORT.Modules.Host;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace GORT.Modules.Agent;

/// <summary>Agent orchestration module, bridging tokens, templates, Compose, and service supervision.</summary>
public sealed class AgentModule : NasModuleBase
{
    /// <inheritdoc />
    public override string ModuleId => "agent";

    /// <inheritdoc />
    public override string DisplayName => "Agent Orchestration";

    /// <inheritdoc />
    public override IReadOnlyList<string> Dependencies => ["storage"];

    /// <inheritdoc />
    public override IReadOnlyList<string> RequiredCapabilities => ["agent:deploy", "agent:control", "service:write"];

    /// <summary>Deploy an agent.</summary>
    public async Task<ServiceDefinition> DeployAgentAsync(string templateId, AgentConfig config, string ownerToken, CancellationToken ct)
    {
        var catalog = RequiredService<IAgentCatalog>();
        var template = await catalog.GetTemplateAsync(templateId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Agent template does not exist: {templateId}");
        var normalizedConfig = NormalizeConfig(config, Services.GetService(typeof(IGortConfiguration)) as IGortConfiguration);
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

    /// <summary>Start an agent.</summary>
    public Task StartAgentAsync(string agentId, CancellationToken ct) => RequiredService<IServiceSupervisor>().StartAsync(ServiceId(agentId), ct);

    /// <summary>Stop an agent.</summary>
    public Task StopAgentAsync(string agentId, CancellationToken ct) => RequiredService<IServiceSupervisor>().StopAsync(ServiceId(agentId), ct);

    /// <summary>Remove an agent.</summary>
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
            Logger.LogWarning(ex, "Error stopping agent {AgentId}, proceeding with deregistration.", agentId);
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

    /// <summary>List agent services.</summary>
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
    /// Normalize agent configuration and fill in default data volumes.
    /// </summary>
    private static AgentConfig NormalizeConfig(AgentConfig config, IGortConfiguration? configuration)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(config.AgentId))
        {
            throw new ArgumentException("AgentId cannot be empty.", nameof(config));
        }

        if (string.IsNullOrWhiteSpace(config.ImageName))
        {
            throw new ArgumentException("ImageName cannot be empty.", nameof(config));
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
            throw new InvalidOperationException("Docker is unavailable, cannot deploy agent. Please verify docker engine and socket are accessible.");
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
            throw new InvalidOperationException($"Failed to pull agent image {imageName}: {pull.Stderr}");
        }
    }

    private static async Task EnsureVolumePathsWritableAsync(IEnumerable<VolumeMapping> mappings, CancellationToken ct)
    {
        foreach (var mapping in mappings)
        {
            Directory.CreateDirectory(mapping.HostPath);
            var probe = Path.Combine(mapping.HostPath, ".gort-write-probe-" + Guid.CreateVersion7().ToString("N"));
            await File.WriteAllTextAsync(probe, "probe", ct).ConfigureAwait(false);
            File.Delete(probe);
        }
    }

    private static VolumeMapping NormalizeVolumeMapping(VolumeMapping mapping, IReadOnlyList<string> allowedRoots)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        if (string.IsNullOrWhiteSpace(mapping.HostPath))
        {
            throw new ArgumentException("Volume mapping host path cannot be empty.", nameof(mapping));
        }

        if (string.IsNullOrWhiteSpace(mapping.ContainerPath))
        {
            throw new ArgumentException("Volume mapping container path cannot be empty.", nameof(mapping));
        }

        if (mapping.HostPath.Contains('\n') || mapping.HostPath.Contains('\r')
            || mapping.ContainerPath.Contains('\n') || mapping.ContainerPath.Contains('\r'))
        {
            throw new ArgumentException("Volume mapping path cannot contain newlines.", nameof(mapping));
        }

        if (!IsAbsolutePath(mapping.HostPath))
        {
            throw new ArgumentException("Volume mapping host path must be an absolute path.", nameof(mapping));
        }

        if (!mapping.ContainerPath.StartsWith("/", StringComparison.Ordinal))
        {
            throw new ArgumentException("Volume mapping container path must be a Unix absolute path.", nameof(mapping));
        }

        var normalizedHostPath = NormalizePath(mapping.HostPath);
        if (!allowedRoots.Any(root => IsPathUnderRoot(normalizedHostPath, root)))
        {
            throw new ArgumentException($"Volume mapping path {mapping.HostPath} is not within an allowed directory.", nameof(mapping));
        }

        return mapping with { HostPath = mapping.HostPath };
    }

    private static void ValidateImage(string imageName, IGortConfiguration? configuration)
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

    private static string[] ResolveAllowedRoots(IGortConfiguration? configuration)
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
        var root = Environment.GetEnvironmentVariable("GORT_DATA_ROOT");
        return string.IsNullOrWhiteSpace(root) ? "/srv/nas" : root;
    }

    private static bool IsAbsolutePath(string path)
        => Path.IsPathFullyQualified(path) || path.StartsWith("/", StringComparison.Ordinal);

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be empty.", nameof(path));
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
