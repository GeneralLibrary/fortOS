namespace GORT.Core;

/// <summary>Compose file generator interface.</summary>
public interface IComposeGenerator
{
    /// <summary>Generate Compose and environment files.</summary>
    Task<ComposeGenerationResult> GenerateAsync(AgentTemplate template, AgentConfig config, string ownerToken, CancellationToken ct);
}
