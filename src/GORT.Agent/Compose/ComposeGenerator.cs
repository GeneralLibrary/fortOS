using System.Globalization;
using GORT.Agent.Infrastructure;
using GORT.Core;
using Scriban;
using Scriban.Runtime;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace GORT.Agent.Compose;

/// <summary>
/// Generates Agent Docker Compose and private environment variable files.
/// </summary>
public sealed class ComposeGenerator : IComposeGenerator
{
    private readonly ITokenBroker _tokenBroker;
    private readonly IGortConfiguration? _configuration;

    /// <summary>
    /// Initialize the Compose generator.
    /// </summary>
    public ComposeGenerator(ITokenBroker tokenBroker, IGortConfiguration? configuration = null)
    {
        _tokenBroker = tokenBroker;
        _configuration = configuration;
    }

    /// <inheritdoc />
    public async Task<ComposeGenerationResult> GenerateAsync(AgentTemplate template, AgentConfig config, string ownerToken, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(config);

        var tokenResult = await _tokenBroker.IssueAgentTokenAsync(config, ownerToken, ct).ConfigureAwait(false);
        var apiEndpoint = _configuration?.GetValue("agent:api_endpoint") ?? Environment.GetEnvironmentVariable("GORT_API_ENDPOINT") ?? "http://host.docker.internal:5000";
        var rendered = RenderTemplate(template, config, tokenResult, apiEndpoint);
        var yaml = ParseYaml(rendered);
        InjectComposeSettings(yaml, config, tokenResult, apiEndpoint);
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
        await File.WriteAllTextAsync(envPath, BuildEnvFile(tokenResult, apiEndpoint), ct).ConfigureAwait(false);
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

    private static void InjectComposeSettings(YamlStream stream, AgentConfig config, AgentTokenResult tokenResult, string apiEndpoint)
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
            ValidateUntrustedService(child);
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
        service.Children.Remove(new YamlScalarNode("volumes"));
        service.Children[new YamlScalarNode("env_file")] = new YamlSequenceNode(new YamlScalarNode(".env"));
        var environment = GetOrAddMapping(service, "environment");
        environment.Children[new YamlScalarNode("NAS_TOKEN")] = new YamlScalarNode("${NAS_TOKEN}");
        environment.Children[new YamlScalarNode("NAS_API_ENDPOINT")] = new YamlScalarNode(apiEndpoint);
        environment.Children[new YamlScalarNode("AGENT_CAPABILITIES")] = new YamlScalarNode(string.Join(',', tokenResult.Capabilities));
        environment.Children[new YamlScalarNode("TZ")] = new YamlScalarNode(Environment.GetEnvironmentVariable("TZ") ?? "UTC");

        if (config.VolumeMapping.Length > 0)
        {
            var volumes = GetOrAddSequence(service, "volumes");
            foreach (var mapping in config.VolumeMapping)
            {
                volumes.Add(new YamlScalarNode($"{mapping.HostPath}:{mapping.ContainerPath}:{(mapping.ReadOnly ? "ro" : "rw")}"));
            }
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

    private static void ValidateUntrustedService(YamlMappingNode service)
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
            foreach (var volume in volumeList.Children.OfType<YamlScalarNode>())
            {
                var source = (volume.Value ?? string.Empty).Split(':', 2)[0];
                if (source == "/" || source.Contains("docker.sock", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Agent compose may not mount the Docker socket or host root.");
            }
        }
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

    private static string BuildEnvFile(AgentTokenResult tokenResult, string apiEndpoint)
        => $"NAS_TOKEN={tokenResult.Token}{Environment.NewLine}NAS_API_ENDPOINT={apiEndpoint}{Environment.NewLine}AGENT_CAPABILITIES={string.Join(',', tokenResult.Capabilities)}{Environment.NewLine}TZ={Environment.GetEnvironmentVariable("TZ") ?? "UTC"}{Environment.NewLine}";

    private static string FormatMemory(long bytes)
    {
        const long mib = 1024 * 1024;
        return bytes % mib == 0 ? $"{bytes / mib}M" : bytes.ToString(CultureInfo.InvariantCulture);
    }

    private static void SetFileMode(string path, UnixFileMode mode)
        => File.SetUnixFileMode(path, mode);
}
