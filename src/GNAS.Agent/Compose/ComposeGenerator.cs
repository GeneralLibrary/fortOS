using System.Globalization;
using GNAS.Agent.Infrastructure;
using GNAS.Core;
using Scriban;
using Scriban.Runtime;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace GNAS.Agent.Compose;

/// <summary>
/// 生成 Agent Docker Compose 与私有环境变量文件。
/// </summary>
public sealed class ComposeGenerator : IComposeGenerator
{
    private readonly ITokenBroker _tokenBroker;
    private readonly IGnasConfiguration? _configuration;

    /// <summary>
    /// 初始化 Compose 生成器。
    /// </summary>
    public ComposeGenerator(ITokenBroker tokenBroker, IGnasConfiguration? configuration = null)
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
        var apiEndpoint = _configuration?.GetValue("agent:api_endpoint") ?? Environment.GetEnvironmentVariable("GNAS_API_ENDPOINT") ?? "http://host.docker.internal:5000";
        var rendered = RenderTemplate(template, config, tokenResult, apiEndpoint);
        var yaml = ParseYaml(rendered);
        InjectComposeSettings(yaml, config, tokenResult, apiEndpoint);
        var composeText = SerializeYaml(yaml);
        if (composeText.Contains(tokenResult.Token, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("生成的 compose.yml 不能包含原始 Agent token。");
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
            throw new InvalidDataException("Compose 模板 Scriban 解析失败：" + string.Join("; ", parsed.Messages.Select(m => m.Message)));
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
            throw new InvalidDataException("Compose YAML 无效。", ex);
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
            throw new InvalidDataException("Compose YAML 根节点必须是映射。");
        }

        var services = GetOrAddMapping(root, "services");
        if (services.Children.Count == 0)
        {
            services.Add(config.AgentId, new YamlMappingNode());
        }

        foreach (var child in services.Children.Values.OfType<YamlMappingNode>())
        {
            InjectService(child, config, tokenResult, apiEndpoint);
        }
    }

    private static void InjectService(YamlMappingNode service, AgentConfig config, AgentTokenResult tokenResult, string apiEndpoint)
    {
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

        if (config.ResourceQuota is not null)
        {
            var limits = GetOrAddMapping(GetOrAddMapping(GetOrAddMapping(service, "deploy"), "resources"), "limits");
            if (config.ResourceQuota.CpuLimit is not null)
            {
                limits.Children[new YamlScalarNode("cpus")] = new YamlScalarNode(config.ResourceQuota.CpuLimit.Value.ToString("0.###", CultureInfo.InvariantCulture));
            }

            if (config.ResourceQuota.MemoryLimitBytes is not null)
            {
                limits.Children[new YamlScalarNode("memory")] = new YamlScalarNode(FormatMemory(config.ResourceQuota.MemoryLimitBytes.Value));
            }
        }
    }

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
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, mode);
        }
    }
}
