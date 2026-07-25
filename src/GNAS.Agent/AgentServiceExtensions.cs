using GNAS.Agent.Broker;
using GNAS.Agent.Catalog;
using GNAS.Agent.Collector;
using GNAS.Agent.Compose;
using GNAS.Core;
using Microsoft.Extensions.DependencyInjection;

namespace GNAS.Agent;

/// <summary>
/// Agent 集成层依赖注入扩展。
/// </summary>
public static class AgentServiceExtensions
{
    /// <summary>
    /// 注册 Agent Catalog、Token Broker、Compose Generator 与后台采集服务。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddAgentServices(this IServiceCollection services)
    {
        services.AddSingleton<AgentTokenRegistry>();
        services.AddSingleton<IAgentCatalog, AgentCatalog>();
        services.AddSingleton<ITokenBroker, TokenBroker>();
        services.AddSingleton<IComposeGenerator, ComposeGenerator>();
        services.AddSingleton<AgentLogCollector>();
        services.AddHostedService(sp => sp.GetRequiredService<AgentLogCollector>());
        services.AddHostedService<TokenRenewalService>();
        return services;
    }
}
