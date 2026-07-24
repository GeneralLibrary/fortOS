using GNAS.Core;
using GNAS.Modules.Host;
using Microsoft.Extensions.Logging;

namespace GNAS.Modules.Agent;

/// <summary>Agent 编排模块，衔接令牌、模板、Compose 与服务监管。</summary>
public sealed class AgentModule : NasModuleBase
{
    /// <inheritdoc />
    public override string ModuleId => "agent";

    /// <inheritdoc />
    public override string DisplayName => "Agent 编排";

    /// <inheritdoc />
    public override IReadOnlyList<string> Dependencies => ["storage"];

    /// <inheritdoc />
    public override IReadOnlyList<string> RequiredCapabilities => ["agent:deploy", "agent:control", "service:write"];

    /// <summary>部署 Agent。</summary>
    public async Task<ServiceDefinition> DeployAgentAsync(string templateId, AgentConfig config, string ownerToken, CancellationToken ct)
    {
        var catalog = RequiredService<IAgentCatalog>();
        var template = await catalog.GetTemplateAsync(templateId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Agent 模板不存在: {templateId}");
        var token = await RequiredService<ITokenBroker>().IssueAgentTokenAsync(config, ownerToken, ct).ConfigureAwait(false);
        var compose = await RequiredService<IComposeGenerator>().GenerateAsync(template, config, token.Token, ct).ConfigureAwait(false);
        var service = new ServiceDefinition
        {
            ServiceId = $"agent-{config.AgentId}",
            DisplayName = config.DisplayName,
            Type = ServiceType.Container,
            ComposeFile = compose.ComposeFilePath,
            RequiredCapabilities = config.Capabilities,
            Startup = ServiceStartup.Manual,
            RestartPolicy = RestartPolicy.OnFailure,
            Quota = config.ResourceQuota
        };
        await RequiredService<IServiceRegistry>().RegisterAsync(service, ct).ConfigureAwait(false);
        await RequiredService<IServiceSupervisor>().StartAsync(service.ServiceId, ct).ConfigureAwait(false);
        await PublishAsync($"agent.{config.AgentId}.deployed", "agent.deployed", new { config.AgentId, templateId, service.ServiceId }, ct).ConfigureAwait(false);
        return service;
    }

    /// <summary>启动 Agent。</summary>
    public Task StartAgentAsync(string agentId, CancellationToken ct) => RequiredService<IServiceSupervisor>().StartAsync(ServiceId(agentId), ct);

    /// <summary>停止 Agent。</summary>
    public Task StopAgentAsync(string agentId, CancellationToken ct) => RequiredService<IServiceSupervisor>().StopAsync(ServiceId(agentId), ct);

    /// <summary>移除 Agent。</summary>
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
            Logger.LogWarning(ex, "停止 Agent {AgentId} 时发生错误，继续注销。", agentId);
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

    /// <summary>列出 Agent 服务。</summary>
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
}
