using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FortOS.Agent.Infrastructure;
using FortOS.Core;
using Scriban;
using Scriban.Runtime;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace FortOS.Agent.Compose;

/// <summary>
/// Generates Agent Docker Compose and private environment variable files.
/// </summary>
public sealed class ComposeGenerator : IComposeGenerator
{
    // The agent id is used verbatim as a directory name under AgentPaths.AgentsRoot and as a
    // compose project name. Refuse anything that could escape the agents root even when a caller
    // bypasses AgentModule's own validation (defense in depth).
    private static readonly Regex AgentIdPattern = new(@"^[a-z][a-z0-9-]{0,63}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ITokenBroker _tokenBroker;
    private readonly IFortOSConfiguration? _configuration;

    /// <summary>
    /// Initialize the Compose generator.
    /// </summary>
    public ComposeGenerator(ITokenBroker tokenBroker, IFortOSConfiguration? configuration = null)
    {
        _tokenBroker = tokenBroker;
        _configuration = configuration;
    }

    /// <inheritdoc />
    public async Task<ComposeGenerationResult> GenerateAsync(AgentTemplate template, AgentConfig config, string ownerToken, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(config);
        if (!AgentIdPattern.IsMatch(config.AgentId))
        {
            throw new ArgumentException($"AgentId must match ^[a-z][a-z0-9-]{{0,63}}$ (got '{config.AgentId}').", nameof(config));
        }

        var tokenResult = await _tokenBroker.IssueAgentTokenAsync(config, ownerToken, ct).ConfigureAwait(false);
        var apiEndpoint = _configuration?.GetValue("agent:api_endpoint") ?? Environment.GetEnvironmentVariable("FortOS_API_ENDPOINT") ?? "http://host.docker.internal:5000";
        var rendered = RenderTemplate(template, config, tokenResult, apiEndpoint);
        var yaml = ParseYaml(rendered);
        InjectComposeSettings(yaml, config, tokenResult, apiEndpoint, AllowedHostRoots());
        var composeText = SerializeYaml(yaml);
        if (composeText.Contains(tokenResult.Token, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The generated compose.yml must not contain the raw Agent token.");
        }

        ParseYaml(composeText);
        var agentDir = Path.Combine(AgentPaths.AgentsRoot, config.AgentId);
        Directory.CreateDirectory(agentDir);
        var composePath = Path.Combine(agentDir, "docker-compose.yml");
        var envPath = Path.Combine(agentDir, ".env");
        await File.WriteAllTextAsync(composePath, composeText, ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(envPath, BuildEnvFile(template, config, tokenResult, apiEndpoint), ct).ConfigureAwait(false);
        SetFileMode(composePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        SetFileMode(envPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return new ComposeGenerationResult
        {
            AgentId = config.AgentId,
            ComposeFilePath = composePath,
            EnvFilePath = envPath,
            Token = tokenResult.Token,
        };
    }

    private static string RenderTemplate(AgentTemplate template, AgentConfig config, AgentTokenResult tokenResult, string apiEndpoint)
    {
        var text = template.ComposeTemplate.Replace("{{.", "{{", StringComparison.Ordinal);
        var parsed = Template.Parse(text);
        if (parsed.HasErrors)
        {
            throw new InvalidDataException("Compose template Scriban parsing failed: " + string.Join("; ", parsed.Messages.Select(m => m.Message)));
        }

        var variables = new ScriptObject();
        variables.SetValue("AgentId", config.AgentId, true);
        variables.SetValue("Version", template.Version, true);
        variables.SetValue("Token", "${NAS_TOKEN}", true);
        variables.SetValue("Capabilities", string.Join(',', config.Capabilities), true);
        variables.SetValue("ApiEndpoint", apiEndpoint, true);
        variables.SetValue("DisplayName", config.DisplayName, true);
        variables.SetValue("ImageName", config.ImageName, true);
        variables.SetValue("TemplateId", template.Id, true);
        variables.SetValue("TemplateName", template.Name, true);
        var context = new TemplateContext();
        context.PushGlobal(variables);
        return parsed.Render(context);
    }

    private static YamlStream ParseYaml(string yaml)
    {
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            return stream;
        }
        catch (YamlException ex)
        {
            throw new InvalidDataException("Compose YAML is invalid.", ex);
        }
    }

    private static string SerializeYaml(YamlStream stream)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        stream.Save(writer, false);
        return writer.ToString();
    }

    private static void InjectComposeSettings(YamlStream stream, AgentConfig config, AgentTokenResult tokenResult, string apiEndpoint, IReadOnlyList<string> allowedRoots)
    {
        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            throw new InvalidDataException("Compose YAML root node must be a mapping.");
        }

        var services = GetOrAddMapping(root, "services");
        if (services.Children.Count == 0)
        {
            services.Add(config.AgentId, new YamlMappingNode());
        }

        foreach (var child in services.Children.Values.OfType<YamlMappingNode>())
        {
            ValidateUntrustedService(child, allowedRoots);
            InjectService(child, config, tokenResult, apiEndpoint);
        }
    }

    private static void InjectService(YamlMappingNode service, AgentConfig config, AgentTokenResult tokenResult, string apiEndpoint)
    {
        // The generated security profile is authoritative; templates cannot relax it.
        service.Children[new YamlScalarNode("image")] = new YamlScalarNode(config.ImageName);
        service.Children[new YamlScalarNode("privileged")] = new YamlScalarNode("false");
        service.Children[new YamlScalarNode("read_only")] = new YamlScalarNode("true");
        service.Children[new YamlScalarNode("security_opt")] = new YamlSequenceNode(new YamlScalarNode("no-new-privileges:true"));
        service.Children[new YamlScalarNode("cap_drop")] = new YamlSequenceNode(new YamlScalarNode("ALL"));
        var existingTmpfs = service.Children.TryGetValue(new YamlScalarNode("tmpfs"), out var tmp) && tmp is YamlSequenceNode seq ? seq : null;
        var mergedTmpfs = new YamlSequenceNode();
        if (existingTmpfs is not null)
        {
            foreach (var child in existingTmpfs.Children)
            {
                mergedTmpfs.Add(child);
            }
        }
        var hasTmp = existingTmpfs?.Children.OfType<YamlScalarNode>().Any(n => (n.Value ?? string.Empty).StartsWith("/tmp:", StringComparison.Ordinal)) == true;
        if (!hasTmp)
        {
            mergedTmpfs.Add(new YamlScalarNode("/tmp:rw,noexec,nosuid,size=64m"));
        }
        service.Children[new YamlScalarNode("tmpfs")] = mergedTmpfs;
        service.Children[new YamlScalarNode("env_file")] = new YamlSequenceNode(new YamlScalarNode(".env"));
        var environment = GetOrAddMapping(service, "environment");
        environment.Children[new YamlScalarNode("NAS_TOKEN")] = new YamlScalarNode("${NAS_TOKEN}");
        environment.Children[new YamlScalarNode("NAS_API_ENDPOINT")] = new YamlScalarNode(apiEndpoint);
        environment.Children[new YamlScalarNode("AGENT_CAPABILITIES")] = new YamlScalarNode(string.Join(',', tokenResult.Capabilities));
        environment.Children[new YamlScalarNode("TZ")] = new YamlScalarNode(Environment.GetEnvironmentVariable("TZ") ?? "UTC");

        // Merge caller volume mappings into the template's own volumes instead of replacing the
        // list: dropping the template volumes would silently remove template-declared data volumes
        // whenever the caller supplies no VolumeMapping, losing data on every container recreation.
        // A caller mapping on the same container path REPLACES the template entry — duplicate
        // targets would otherwise make Docker silently pick one of the two mounts.
        var volumes = GetOrAddSequence(service, "volumes");
        var templateEntries = new Dictionary<string, YamlNode>(StringComparer.Ordinal);
        foreach (var child in volumes.Children)
        {
            if (GetVolumeTarget(child) is { } target)
            {
                templateEntries[target] = child;
            }
        }

        foreach (var mapping in config.VolumeMapping)
        {
            if (templateEntries.Remove(mapping.ContainerPath))
            {
                volumes.Children.Remove(templateEntries[mapping.ContainerPath]);
            }

            volumes.Add(new YamlScalarNode($"{mapping.HostPath}:{mapping.ContainerPath}:{(mapping.ReadOnly ? "ro" : "rw")}"));
        }

        if (config.PortMapping.Length > 0)
        {
            var ports = GetOrAddSequence(service, "ports");
            foreach (var mapping in config.PortMapping)
            {
                ports.Add(new YamlScalarNode($"{mapping.HostPort}:{mapping.ContainerPort}/{mapping.Protocol}"));
            }
        }

        {
            var limits = GetOrAddMapping(GetOrAddMapping(GetOrAddMapping(service, "deploy"), "resources"), "limits");
            limits.Children[new YamlScalarNode("cpus")] = new YamlScalarNode((config.ResourceQuota?.CpuLimit ?? 1d).ToString("0.###", CultureInfo.InvariantCulture));
            limits.Children[new YamlScalarNode("memory")] = new YamlScalarNode(FormatMemory(config.ResourceQuota?.MemoryLimitBytes ?? 512L * 1024 * 1024));
        }
    }

    private static void ValidateUntrustedService(YamlMappingNode service, IReadOnlyList<string> allowedRoots)
    {
        RejectTrue(service, "privileged");
        RejectHostNamespace(service, "network_mode");
        RejectHostNamespace(service, "pid");
        RejectHostNamespace(service, "ipc");
        RejectPresent(service, "devices");
        if (service.Children.TryGetValue(new YamlScalarNode("cap_add"), out var caps) && caps is YamlSequenceNode capList
            && capList.Children.OfType<YamlScalarNode>().Any(c => IsDangerousCapability(c.Value)))
            throw new InvalidDataException("Agent compose may not add dangerous Linux capabilities.");
        if (service.Children.TryGetValue(new YamlScalarNode("volumes"), out var volumes) && volumes is YamlSequenceNode volumeList)
        {
            foreach (var volume in volumeList.Children)
            {
                // Both compose short-form ("host:container:ro") and long-form (type/source/target
                // mapping) entries must be checked: the long form is a YamlMappingNode.
                string? source;
                if (volume is YamlScalarNode scalar)
                {
                    source = (scalar.Value ?? string.Empty).Split(':', 2)[0];
                }
                else if (volume is YamlMappingNode mapping
                    && mapping.Children.TryGetValue(new YamlScalarNode("source"), out var src) && src is YamlScalarNode srcScalar)
                {
                    source = srcScalar.Value;
                }
                else
                {
                    continue; // Anonymous volumes / entries without a source have nothing to validate.
                }

                if (string.IsNullOrWhiteSpace(source)) continue;
                if (source == "/" || source.Contains("docker.sock", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Agent compose may not mount the Docker socket or host root.");
                if (source.StartsWith("/", StringComparison.Ordinal))
                {
                    // Absolute-path sources are host bind mounts: they must stay within the allowed
                    // roots, mirroring the AgentModule.NormalizeVolumeMapping check for caller
                    // mappings so a malicious template cannot mount arbitrary host paths.
                    if (!allowedRoots.Any(root => FortOS.Core.PathSafety.IsPathUnderRoot(source, root)))
                        throw new InvalidDataException($"Agent compose volume source {source} is not within an allowed directory.");
                }
                else if (source is "." or ".." || source.Contains('/'))
                {
                    // Relative sources ("./data", "../..", "." / "..") resolve against the compose
                    // project directory and can traverse out of it; only pure named volumes (no
                    // slash) are acceptable from templates.
                    throw new InvalidDataException($"Agent compose volume source '{source}' must be an absolute path within an allowed directory.");
                }
            }
        }
    }

    /// <summary>
    /// Extracts the container-side target of a volume entry in either compose syntax
    /// (short-form "source:target[:mode]" or long-form mapping with a "target" key).
    /// </summary>
    private static string? GetVolumeTarget(YamlNode volume)
    {
        if (volume is YamlScalarNode scalar)
        {
            var parts = (scalar.Value ?? string.Empty).Split(':', 3);
            return parts.Length >= 2 ? parts[1] : null;
        }

        if (volume is YamlMappingNode mapping
            && mapping.Children.TryGetValue(new YamlScalarNode("target"), out var target) && target is YamlScalarNode targetScalar)
        {
            return targetScalar.Value;
        }

        return null;
    }

    /// <summary>
    /// Resolves the host-path roots allowed for volume bind mounts, using the same configuration
    /// keys (and the same data-root fallback) as AgentModule.ResolveAllowedRoots.
    /// </summary>
    private IReadOnlyList<string> AllowedHostRoots()
    {
        var configured = _configuration?.GetArray("agent:allowed_volume_roots") ?? [];
        if (configured.Length == 0 && _configuration?.GetValue("agent:allowed_volume_roots") is { Length: > 0 } value)
        {
            configured = value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        var roots = configured.Length == 0
            ? [PathSafety.ResolveDataRoot(Environment.GetEnvironmentVariable("FortOS_DATA_ROOT"))]
            : configured;

        return roots.Select(PathSafety.NormalizePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void RejectTrue(YamlMappingNode service, string key)
    {
        if (service.Children.TryGetValue(new YamlScalarNode(key), out var value) && value is YamlScalarNode scalar && bool.TryParse(scalar.Value, out var enabled) && enabled)
            throw new InvalidDataException($"Agent compose may not enable {key}.");
    }

    private static void RejectHostNamespace(YamlMappingNode service, string key)
    {
        if (service.Children.TryGetValue(new YamlScalarNode(key), out var value) && value is YamlScalarNode scalar && string.Equals(scalar.Value, "host", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Agent compose may not use host {key}.");
    }

    private static void RejectPresent(YamlMappingNode service, string key)
    {
        if (service.Children.ContainsKey(new YamlScalarNode(key))) throw new InvalidDataException($"Agent compose may not define {key}.");
    }

    private static bool IsDangerousCapability(string? capability) => capability?.ToUpperInvariant() is "ALL" or "SYS_ADMIN" or "SYS_MODULE" or "SYS_PTRACE" or "NET_ADMIN" or "DAC_OVERRIDE";

    private static YamlMappingNode GetOrAddMapping(YamlMappingNode parent, string key)
    {
        var keyNode = new YamlScalarNode(key);
        if (parent.Children.TryGetValue(keyNode, out var existing) && existing is YamlMappingNode mapping)
        {
            return mapping;
        }

        mapping = new YamlMappingNode();
        parent.Children[keyNode] = mapping;
        return mapping;
    }

    private static YamlSequenceNode GetOrAddSequence(YamlMappingNode parent, string key)
    {
        var keyNode = new YamlScalarNode(key);
        if (parent.Children.TryGetValue(keyNode, out var existing) && existing is YamlSequenceNode sequence)
        {
            return sequence;
        }

        sequence = new YamlSequenceNode();
        parent.Children[keyNode] = sequence;
        return sequence;
    }

    private static readonly HashSet<string> ReservedEnvNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "image", "data_dir", "NAS_TOKEN", "NAS_API_ENDPOINT", "AGENT_CAPABILITIES", "TZ",
    };

    // Environment variable names must be POSIX-safe identifiers; anything else (whitespace,
    // '=', newlines, '.' …) could break out of a single .env entry and inject arbitrary lines
    // into the compose environment.
    private static readonly System.Text.RegularExpressions.Regex EnvNamePattern = new("^[A-Za-z_][A-Za-z0-9_]*$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string BuildEnvFile(AgentTemplate template, AgentConfig config, AgentTokenResult tokenResult, string apiEndpoint)
    {
        var sb = new StringBuilder();
        sb.Append("NAS_TOKEN=").Append(tokenResult.Token).Append(Environment.NewLine);
        sb.Append("NAS_API_ENDPOINT=").Append(apiEndpoint).Append(Environment.NewLine);
        sb.Append("AGENT_CAPABILITIES=").Append(string.Join(',', tokenResult.Capabilities)).Append(Environment.NewLine);
        sb.Append("TZ=").Append(Environment.GetEnvironmentVariable("TZ") ?? "UTC").Append(Environment.NewLine);

        // Template parameter defaults (except reserved names) become .env entries so the
        // rendered compose can reference them via ${NAME} — e.g. host ports, API keys.
        foreach (var parameter in template.Parameters)
        {
            if (ReservedEnvNames.Contains(parameter.Name) || string.IsNullOrWhiteSpace(parameter.Default))
            {
                continue;
            }

            if (!EnvNamePattern.IsMatch(parameter.Name))
            {
                // Same exception type as the user-env validation below so both paths are
                // mapped to 400 by FortOSExceptionFilter (InvalidDataException would 500).
                throw new ArgumentException($"Template parameter '{parameter.Name}' is not a valid environment variable name.", nameof(template));
            }

            sb.Append(parameter.Name).Append('=').Append(SanitizeEnvValue(parameter.Default)).Append(Environment.NewLine);
        }

        // User-provided environment overrides win over template defaults.
        foreach (var pair in config.Environment)
        {
            if (ReservedEnvNames.Contains(pair.Key) || !EnvNamePattern.IsMatch(pair.Key))
            {
                // Reject, rather than silently drop, malformed names: a name with a newline
                // or '=' would otherwise inject arbitrary content into the generated .env.
                throw new ArgumentException($"Invalid environment variable name '{pair.Key}'.", nameof(config));
            }

            sb.Append(pair.Key).Append('=').Append(SanitizeEnvValue(pair.Value)).Append(Environment.NewLine);
        }

        return sb.ToString();
    }

    private static string SanitizeEnvValue(string value)
        => value.ReplaceLineEndings(" ").Replace("\0", "");

    private static string FormatMemory(long bytes)
    {
        const long mib = 1024 * 1024;
        return bytes % mib == 0 ? $"{bytes / mib}M" : bytes.ToString(CultureInfo.InvariantCulture);
    }

    private static void SetFileMode(string path, UnixFileMode mode)
    {
        // File.SetUnixFileMode is Linux-only; on Windows (local dev, CI) the compose file
        // is still written successfully but the restrictive permission cannot be applied.
        // Deployment targets are Linux, where this always runs.
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        File.SetUnixFileMode(path, mode);
    }
}
