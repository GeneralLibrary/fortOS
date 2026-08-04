using System.Text.Json;
using System.Text.RegularExpressions;
using FortOS.Agent.Infrastructure;
using FortOS.Core;
using FortOS.Modules.Host;
using Microsoft.Extensions.Logging;

namespace FortOS.Modules.Agent;

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
        var normalizedConfig = NormalizeConfig(config, Services.GetService(typeof(IFortOSConfiguration)) as IFortOSConfiguration);
        normalizedConfig = ApplyTemplateVolumes(template, normalizedConfig);
        await RunPreflightChecksAsync(template, normalizedConfig, ct).ConfigureAwait(false);
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
        await PersistAgentManifestAsync(template, normalizedConfig, ct).ConfigureAwait(false);
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
    public Task StartAgentAsync(string agentId, CancellationToken ct) => RequiredService<IServiceSupervisor>().StartAsync(ServiceId(ValidateAgentId(agentId)), ct);

    /// <summary>Stop an agent.</summary>
    public Task StopAgentAsync(string agentId, CancellationToken ct) => RequiredService<IServiceSupervisor>().StopAsync(ServiceId(ValidateAgentId(agentId)), ct);

    /// <summary>Remove an agent.</summary>
    public async Task RemoveAgentAsync(string agentId, CancellationToken ct)
    {
        ValidateAgentId(agentId);
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

        try
        {
            await registry.UnregisterAsync(serviceId, ct).ConfigureAwait(false);
        }
        catch (ServiceNotFoundException)
        {
            // Service registration is in-memory and lost on restart; keep cleaning up anyway.
        }

        // Release supervisor-held resources (container host, status entries, health checks) so the
        // service disappears from the Services list as well.
        try
        {
            await supervisor.RemoveAsync(serviceId, ct).ConfigureAwait(false);
        }
        catch (ServiceNotFoundException)
        {
            // Already removed.
        }
        try
        {
            await RequiredService<ITokenBroker>().RevokeAgentTokenAsync(agentId, "agent removed", ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Token registry is in-memory and lost on restart; never block removal on token revocation.
            Logger.LogWarning(ex, "Error revoking agent token for {AgentId}, continuing removal.", agentId);
        }

        // Remove the deployment directory and the data volume so no agent resources are left behind.
        var dir = Path.Combine(AgentPaths.AgentsRoot, agentId);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }

        var dataDir = Path.Combine(AgentPaths.DataRoot, "agents-data", agentId);
        if (Directory.Exists(dataDir))
        {
            Directory.Delete(dataDir, recursive: true);
        }

        await PublishAsync($"agent.{agentId}.removed", "agent.removed", new { agentId }, ct).ConfigureAwait(false);
    }

    /// <summary>List agent services.</summary>
    public async Task<IReadOnlyList<ServiceDefinition>> ListAgentsAsync(CancellationToken ct)
    {
        var services = await RequiredService<IServiceRegistry>().ListAsync(ct).ConfigureAwait(false);
        return services.Where(s => s.ServiceId.StartsWith("agent-", StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// Returns the persisted deployment manifest (ports, environment variable names,
    /// external access notes) for a deployed agent so the UI can surface how to reach
    /// the agent from outside and how to wire chat channels / clients.
    /// </summary>
    public async Task<AgentAccessInfo> GetAgentAccessAsync(string agentId, CancellationToken ct)
    {
        var id = NormalizeAgentId(agentId);
        ValidateAgentId(id);
        var path = Path.Combine(AgentPaths.AgentsRoot, id, "agent.json");
        if (!File.Exists(path))
        {
            throw new Core.ServiceNotFoundException($"Agent {id} has no access manifest. Deploy the agent first.", "AGENT_MANIFEST_MISSING");
        }

        var manifest = JsonSerializer.Deserialize<AgentAccessInfo>(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false), JsonOptions)
            ?? throw new Core.ServiceNotFoundException($"Agent {id} manifest is empty.", "AGENT_MANIFEST_MISSING");
        var template = await RequiredService<IAgentCatalog>().GetTemplateAsync(manifest.TemplateId, ct).ConfigureAwait(false);
        if (template is not null && template.AccessNotes.Length > 0)
        {
            manifest = manifest with { AccessNotes = template.AccessNotes };
        }

        return manifest;
    }

    private static string NormalizeAgentId(string agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        return agentId.StartsWith("agent-", StringComparison.OrdinalIgnoreCase) ? agentId[6..] : agentId;
    }

    /// <summary>
    /// Writes agent.json next to the compose file: the port mappings that were deployed
    /// (explicit or inferred from the template's host_port/container_port parameters) and
    /// the environment variable names the user can edit in the agent .env file.
    /// </summary>
    private static async Task PersistAgentManifestAsync(AgentTemplate template, AgentConfig config, CancellationToken ct)
    {
        var manifest = new AgentAccessInfo
        {
            AgentId = config.AgentId,
            TemplateId = template.Id,
            ImageName = config.ImageName,
            DisplayName = config.DisplayName,
            Ports = BuildPortInfo(config, template),
            Env = BuildEnvManifest(template, config),
        };
        var dir = Path.Combine(AgentPaths.AgentsRoot, config.AgentId);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "agent.json"), JsonSerializer.Serialize(manifest, JsonOptions), ct).ConfigureAwait(false);
    }

    private static AgentPortInfo[] BuildPortInfo(AgentConfig config, AgentTemplate template)
    {
        if (config.PortMapping.Length > 0)
        {
            return config.PortMapping.Select(p => new AgentPortInfo(p.HostPort, p.ContainerPort, p.Protocol)).ToArray();
        }

        var host = ReadIntParameter(template, "host_port");
        if (host is null)
        {
            return [];
        }

        var container = ReadIntParameter(template, "container_port") ?? host.Value;
        return [new AgentPortInfo(host.Value, container, "tcp")];
    }

    private static AgentEnvInfo[] BuildEnvManifest(AgentTemplate template, AgentConfig config)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in template.Parameters)
        {
            if (!ManifestReservedNames.Contains(parameter.Name))
            {
                names.Add(parameter.Name);
            }
        }

        foreach (var key in config.Environment.Keys)
        {
            if (!ManifestReservedNames.Contains(key))
            {
                names.Add(key);
            }
        }

        return names.Select(name =>
        {
            var parameter = template.Parameters.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            var set = config.Environment.ContainsKey(name) || (parameter is not null && !string.IsNullOrWhiteSpace(parameter.Default));
            return new AgentEnvInfo(name, set);
        }).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static int? ReadIntParameter(AgentTemplate template, string name)
    {
        var parameter = template.Parameters.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        return parameter is not null && int.TryParse(parameter.Default, out var value) ? value : null;
    }

    private static readonly HashSet<string> ManifestReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "image", "data_dir", "NAS_TOKEN", "NAS_API_ENDPOINT", "AGENT_CAPABILITIES", "TZ",
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static string ServiceId(string agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        return agentId.StartsWith("agent-", StringComparison.OrdinalIgnoreCase) ? agentId : $"agent-{agentId}";
    }

    // Agent ids are used verbatim as directory names under AgentPaths.AgentsRoot and as
    // docker-compose project names. A hostile id such as ".." or "../.." would escape the
    // agents root and allow arbitrary directory deletion (RemoveAgentAsync) or file writes
    // (ComposeGenerator). Enforce the same DNS-style charset used for catalog template ids
    // at every entry point that reaches the filesystem or Compose.
    private static readonly Regex AgentIdPattern = new(@"^[a-z][a-z0-9-]{0,63}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Validates an agent id is safe to use as a filesystem/compose project name.</summary>
    /// <exception cref="ArgumentException">When the id contains characters outside <c>^[a-z][a-z0-9-]{0,63}$</c>.</exception>
    private static string ValidateAgentId(string agentId)
    {
        if (!AgentIdPattern.IsMatch(agentId))
        {
            throw new ArgumentException($"AgentId must match ^[a-z][a-z0-9-]{{0,63}}$ (got '{agentId}').", nameof(agentId));
        }

        return agentId;
    }

    /// <summary>
    /// Normalize agent configuration and fill in default data volumes.
    /// </summary>
    private static AgentConfig NormalizeConfig(AgentConfig config, IFortOSConfiguration? configuration)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(config.AgentId))
        {
            throw new ArgumentException("AgentId cannot be empty.", nameof(config));
        }

        ValidateAgentId(config.AgentId);

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

    /// <summary>
    /// When no explicit volumes were supplied, point the default data volume at the
    /// container path the template declares via its <c>data_dir</c> parameter, so
    /// application state persists on the FortOS data root.
    /// </summary>
    private static AgentConfig ApplyTemplateVolumes(AgentTemplate template, AgentConfig config)
    {
        if (config.VolumeMapping.Length == 0)
        {
            return config;
        }

        var dataDir = template.Parameters
            .FirstOrDefault(p => string.Equals(p.Name, "data_dir", StringComparison.OrdinalIgnoreCase))
            ?.Default;
        if (string.IsNullOrWhiteSpace(dataDir) || string.Equals(dataDir, "/data", StringComparison.Ordinal))
        {
            return config;
        }

        var volume = config.VolumeMapping[0];
        return config with { VolumeMapping = [volume with { ContainerPath = dataDir }] };
    }

    private async Task RunPreflightChecksAsync(AgentTemplate template, AgentConfig config, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(template);
        var processManager = RequiredService<IProcessManager>();

        await EnsureDockerAvailableAsync(processManager, ct).ConfigureAwait(false);
        await WriteTemplateConfigAsync(template, config, ct).ConfigureAwait(false);
        await EnsureDataDirOwnershipAsync(template, config, processManager, ct).ConfigureAwait(false);
        await EnsureVolumePathsWritableAsync(config.VolumeMapping, ct).ConfigureAwait(false);
        await EnsureImagePullableAsync(processManager, config.ImageName, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes an initial configuration file into the agent data volume when the template
    /// declares <c>config_file</c> / <c>config_content</c> parameters — e.g. OpenClaw's
    /// openclaw.json with gateway.mode=local so the container boots without interactive setup.
    /// </summary>
    private async Task WriteTemplateConfigAsync(AgentTemplate template, AgentConfig config, CancellationToken ct)
    {
        var fileParameter = template.Parameters.FirstOrDefault(p => string.Equals(p.Name, "config_file", StringComparison.OrdinalIgnoreCase));
        var contentParameter = template.Parameters.FirstOrDefault(p => string.Equals(p.Name, "config_content", StringComparison.OrdinalIgnoreCase));
        if (fileParameter is null || contentParameter is null || string.IsNullOrWhiteSpace(contentParameter.Default))
        {
            return;
        }

        var dataDir = config.VolumeMapping.Length > 0
            ? config.VolumeMapping[0].HostPath
            : Path.Combine(AgentPaths.AgentsRoot, config.AgentId);
        Directory.CreateDirectory(dataDir);
        var configPath = Path.Combine(dataDir, fileParameter.Default!);
        await File.WriteAllTextAsync(configPath, contentParameter.Default, ct).ConfigureAwait(false);
        Logger.LogInformation("Wrote initial configuration for agent {AgentId} to {ConfigPath}.", config.AgentId, configPath);
    }

    /// <summary>
    /// When the template declares a <c>data_uid</c> parameter, chown the data volume
    /// host directories to that uid:gid so containers running as a non-root user
    /// (e.g. OpenClaw's node user, uid 1000) can write their state files.
    /// </summary>
    private static async Task EnsureDataDirOwnershipAsync(AgentTemplate template, AgentConfig config, IProcessManager processManager, CancellationToken ct)
    {
        var uidParameter = template.Parameters.FirstOrDefault(p => string.Equals(p.Name, "data_uid", StringComparison.OrdinalIgnoreCase));
        if (uidParameter is null || !int.TryParse(uidParameter.Default, out var uid))
        {
            return;
        }

        var gid = uid;
        var gidParameter = template.Parameters.FirstOrDefault(p => string.Equals(p.Name, "data_gid", StringComparison.OrdinalIgnoreCase));
        if (gidParameter is not null && int.TryParse(gidParameter.Default, out var parsedGid))
        {
            gid = parsedGid;
        }

        foreach (var mapping in config.VolumeMapping)
        {
            Directory.CreateDirectory(mapping.HostPath);
            var result = await processManager.ExecuteCommandAsync(new ProcessStartConfig
            {
                ExecutablePath = "chown",
                Arguments = $"-R {uid}:{gid} {Quote(mapping.HostPath)}",
                TimeoutSeconds = 60,
            }, ct).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException($"Failed to set ownership on {mapping.HostPath}: {result.Stderr}");
            }
        }
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
        // Large agent images (e.g. OpenClaw, Open WebUI) can take many minutes on slow links.
        var pull = await processManager.ExecuteCommandAsync(new ProcessStartConfig
        {
            ExecutablePath = "docker",
            Arguments = $"pull {Quote(imageName)}",
            TimeoutSeconds = 1800,
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
            var probe = Path.Combine(mapping.HostPath, ".fortos-write-probe-" + Guid.CreateVersion7().ToString("N"));
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

    private static void ValidateImage(string imageName, IFortOSConfiguration? configuration)
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

    private static string[] ResolveAllowedRoots(IFortOSConfiguration? configuration)
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
        var root = Environment.GetEnvironmentVariable("FortOS_DATA_ROOT");
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
