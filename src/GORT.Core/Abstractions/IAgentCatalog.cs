namespace GORT.Core;

/// <summary>Agent template catalog interface.</summary>
public interface IAgentCatalog
{
    /// <summary>List templates.</summary>
    Task<IReadOnlyList<AgentTemplate>> ListTemplatesAsync(CancellationToken ct);
    /// <summary>Get a template.</summary>
    Task<AgentTemplate?> GetTemplateAsync(string templateId, CancellationToken ct);
    /// <summary>Search templates.</summary>
    Task<IReadOnlyList<AgentTemplate>> SearchTemplatesAsync(string query, CancellationToken ct);
    /// <summary>Install a template.</summary>
    Task<AgentTemplate> InstallTemplateAsync(string source, CancellationToken ct);
    /// <summary>Update a template.</summary>
    Task<AgentTemplate> UpdateTemplateAsync(string templateId, CancellationToken ct);
}
