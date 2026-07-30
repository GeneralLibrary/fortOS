using GORT.Agent.Broker;
using GORT.Agent.Catalog;
using GORT.Agent.Collector;
using GORT.Agent.Compose;
using GORT.Core;
using Microsoft.Extensions.DependencyInjection;

namespace GORT.Agent;

/// <summary>
/// Agent integration layer dependency injection extensions.
/// </summary>
public static class AgentServiceExtensions
{
    /// <summary>
    /// Registers Agent Catalog, Token Broker, Compose Generator, and background collection services.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>Service collection.</returns>
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
