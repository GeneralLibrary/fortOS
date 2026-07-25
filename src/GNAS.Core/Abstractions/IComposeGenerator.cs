namespace GNAS.Core;

/// <summary>Compose 文件生成器接口。</summary>
public interface IComposeGenerator
{
    /// <summary>生成 Compose 与环境文件。</summary>
    Task<ComposeGenerationResult> GenerateAsync(AgentTemplate template, AgentConfig config, string ownerToken, CancellationToken ct);
}
