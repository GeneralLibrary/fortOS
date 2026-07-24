namespace GNAS.Core;

/// <summary>Agent 模板目录接口。</summary>
public interface IAgentCatalog
{
    /// <summary>列出模板。</summary>
    Task<IReadOnlyList<AgentTemplate>> ListTemplatesAsync(CancellationToken ct);
    /// <summary>获取模板。</summary>
    Task<AgentTemplate?> GetTemplateAsync(string templateId, CancellationToken ct);
    /// <summary>搜索模板。</summary>
    Task<IReadOnlyList<AgentTemplate>> SearchTemplatesAsync(string query, CancellationToken ct);
    /// <summary>安装模板。</summary>
    Task<AgentTemplate> InstallTemplateAsync(string source, CancellationToken ct);
    /// <summary>更新模板。</summary>
    Task<AgentTemplate> UpdateTemplateAsync(string templateId, CancellationToken ct);
}
