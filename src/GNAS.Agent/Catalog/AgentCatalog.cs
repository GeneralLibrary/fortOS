using System.Text.RegularExpressions;
using GNAS.Agent.Infrastructure;
using GNAS.Core;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace GNAS.Agent.Catalog;

/// <summary>
/// 基于本地 YAML 文件的 Agent 模板目录。
/// </summary>
public sealed partial class AgentCatalog : IAgentCatalog
{
    private static readonly Regex IdPattern = AgentIdRegex();
    private static readonly IReadOnlyDictionary<string, string> BuiltInTemplates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["nginx-basic"] = """
id: nginx-basic
name: Nginx Basic
version: 1.0.0
description: Minimal nginx static server template.
capabilities_required:
  - agent:deploy
parameters:
  - name: image
    type: string
    required: false
    default: nginx:alpine
compose:
  services:
    {{.AgentId}}:
      image: "{{.ImageName}}"
      restart: unless-stopped
      tmpfs:
        - /var/cache/nginx:rw,noexec,nosuid,size=64m
      labels:
        gnas.template: nginx-basic
""",
        ["alpine-worker"] = """
id: alpine-worker
name: Alpine Worker
version: 1.0.0
description: Minimal long-running worker template.
capabilities_required:
  - agent:deploy
parameters:
  - name: image
    type: string
    required: false
    default: alpine:3.20
compose:
  services:
    {{.AgentId}}:
      image: "{{.ImageName}}"
      command: ["/bin/sh", "-c", "while true; do sleep 3600; done"]
      restart: unless-stopped
      labels:
        gnas.template: alpine-worker
""",
    };
    private readonly HttpClient _httpClient;
    private readonly IDeserializer _deserializer;
    private readonly ISerializer _serializer;
    private readonly SemaphoreSlim _seedLock = new(1, 1);
    private int _seeded;

    /// <summary>
    /// 初始化 Agent 模板目录。
    /// </summary>
    /// <param name="httpClient">可选 HTTP 客户端。</param>
    public AgentCatalog(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _deserializer = new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).Build();
        _serializer = new SerializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).Build();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentTemplate>> ListTemplatesAsync(CancellationToken ct)
    {
        await EnsureBuiltInTemplatesAsync(ct).ConfigureAwait(false);
        if (!Directory.Exists(AgentPaths.CatalogRoot))
        {
            return [];
        }

        var templates = new List<AgentTemplate>();
        foreach (var path in Directory.EnumerateFiles(AgentPaths.CatalogRoot, "*.template.yaml").Order(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            templates.Add(await LoadTemplateAsync(path, ct).ConfigureAwait(false));
        }

        return templates;
    }

    /// <inheritdoc />
    public async Task<AgentTemplate?> GetTemplateAsync(string templateId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        await EnsureBuiltInTemplatesAsync(ct).ConfigureAwait(false);
        var path = Path.Combine(AgentPaths.CatalogRoot, templateId + ".template.yaml");
        return File.Exists(path) ? await LoadTemplateAsync(path, ct).ConfigureAwait(false) : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentTemplate>> SearchTemplatesAsync(string query, CancellationToken ct)
    {
        await EnsureBuiltInTemplatesAsync(ct).ConfigureAwait(false);
        var needle = query ?? string.Empty;
        var templates = await ListTemplatesAsync(ct).ConfigureAwait(false);
        return [.. templates.Where(t => Contains(t.Id, needle) || Contains(t.Name, needle) || Contains(t.Description, needle))];
    }

    /// <inheritdoc />
    public async Task<AgentTemplate> InstallTemplateAsync(string source, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        await EnsureBuiltInTemplatesAsync(ct).ConfigureAwait(false);
        var yaml = await ReadSourceAsync(source, ct).ConfigureAwait(false);
        var template = ParseAndValidate(yaml, source);
        Directory.CreateDirectory(AgentPaths.CatalogRoot);
        var destination = Path.Combine(AgentPaths.CatalogRoot, template.Id + ".template.yaml");
        await File.WriteAllTextAsync(destination, yaml, ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(GetSourcePath(template.Id), source, ct).ConfigureAwait(false);
        return template;
    }

    /// <inheritdoc />
    public async Task<AgentTemplate> UpdateTemplateAsync(string templateId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        await EnsureBuiltInTemplatesAsync(ct).ConfigureAwait(false);
        var sourcePath = GetSourcePath(templateId);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"模板 {templateId} 没有可更新的来源记录。", sourcePath);
        }

        var source = (await File.ReadAllTextAsync(sourcePath, ct).ConfigureAwait(false)).Trim();
        var template = await InstallTemplateAsync(source, ct).ConfigureAwait(false);
        if (!string.Equals(template.Id, templateId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"来源模板标识 {template.Id} 与请求标识 {templateId} 不一致。");
        }

        return template;
    }

    private async Task<AgentTemplate> LoadTemplateAsync(string path, CancellationToken ct)
    {
        var yaml = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        return ParseAndValidate(yaml, path);
    }

    private async Task<string> ReadSourceAsync(string source, CancellationToken ct)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme is "http" or "https")
            {
                return await _httpClient.GetStringAsync(uri, ct).ConfigureAwait(false);
            }

            if (uri.Scheme != Uri.UriSchemeFile)
            {
                throw new NotSupportedException($"不支持的模板来源协议：{uri.Scheme}。");
            }
        }

        var path = uri?.Scheme == Uri.UriSchemeFile ? uri.LocalPath : source;
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("模板来源文件不存在。", path);
        }

        return await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
    }

    private AgentTemplate ParseAndValidate(string yaml, string sourceName)
    {
        try
        {
            var dto = _deserializer.Deserialize<TemplateDto>(yaml) ?? throw new InvalidOperationException("模板为空。");
            var compose = _serializer.Serialize(dto.Compose ?? throw new InvalidOperationException("模板缺少 compose。"));
            var template = new AgentTemplate
            {
                Id = dto.Id ?? string.Empty,
                Name = dto.Name ?? string.Empty,
                Version = dto.Version ?? string.Empty,
                Description = dto.Description,
                CapabilitiesRequired = dto.CapabilitiesRequired ?? [],
                Parameters = dto.Parameters?.Select(static p => new AgentTemplateParameter
                {
                    Name = p.Name ?? string.Empty,
                    Type = p.Type ?? string.Empty,
                    Required = p.Required,
                    Default = p.Default,
                }).ToArray() ?? [],
                ComposeTemplate = compose,
            };

            Validate(template);
            return template;
        }
        catch (YamlException ex)
        {
            throw new InvalidDataException($"模板 YAML 解析失败：{sourceName}。", ex);
        }
    }

    private static void Validate(AgentTemplate template)
    {
        if (string.IsNullOrWhiteSpace(template.Id) || string.IsNullOrWhiteSpace(template.Name) || string.IsNullOrWhiteSpace(template.Version) || string.IsNullOrWhiteSpace(template.ComposeTemplate))
        {
            throw new InvalidDataException("模板缺少 id、name、version 或 compose 必填字段。");
        }

        if (!IdPattern.IsMatch(template.Id))
        {
            throw new InvalidDataException("模板 id 必须匹配 ^[a-z][a-z0-9-]{1,63}$。");
        }

        if (!System.Version.TryParse(template.Version, out _))
        {
            throw new InvalidDataException("模板 version 必须是可解析版本号。");
        }

        foreach (var parameter in template.Parameters)
        {
            if (string.IsNullOrWhiteSpace(parameter.Name) || string.IsNullOrWhiteSpace(parameter.Type))
            {
                throw new InvalidDataException("模板参数必须包含 name 和 type。");
            }
        }
    }

    private static bool Contains(string? value, string query) => value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;

    private static string GetSourcePath(string templateId) => Path.Combine(AgentPaths.CatalogRoot, templateId + ".source");

    /// <summary>
    /// 首次访问模板目录时自动写入最小内置模板，避免空目录导致无法开箱部署。
    /// </summary>
    private async Task EnsureBuiltInTemplatesAsync(CancellationToken ct)
    {
        if (Volatile.Read(ref _seeded) == 1)
        {
            return;
        }

        await _seedLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_seeded == 1)
            {
                return;
            }

            Directory.CreateDirectory(AgentPaths.CatalogRoot);
            if (!Directory.EnumerateFiles(AgentPaths.CatalogRoot, "*.template.yaml", SearchOption.TopDirectoryOnly).Any())
            {
                foreach (var pair in BuiltInTemplates)
                {
                    var destination = Path.Combine(AgentPaths.CatalogRoot, pair.Key + ".template.yaml");
                    await File.WriteAllTextAsync(destination, pair.Value, ct).ConfigureAwait(false);
                }
            }

            _seeded = 1;
        }
        finally
        {
            _seedLock.Release();
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9-]{1,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex AgentIdRegex();

    private sealed class TemplateDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Version { get; set; }
        public string? Description { get; set; }
        public string[]? CapabilitiesRequired { get; set; }
        public ParameterDto[]? Parameters { get; set; }
        public object? Compose { get; set; }
    }

    private sealed class ParameterDto
    {
        public string? Name { get; set; }
        public string? Type { get; set; }
        public bool Required { get; set; }
        public string? Default { get; set; }
    }
}
