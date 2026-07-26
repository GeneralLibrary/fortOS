namespace GNAS.Core;

/// <summary>Network management abstraction.</summary>
public interface INetworkManager
{
    /// <summary>List network interfaces.</summary>
    Task<IReadOnlyList<NetworkInterfaceInfo>> ListInterfacesAsync(CancellationToken ct);
    /// <summary>Configure a network interface.</summary>
    Task ConfigureInterfaceAsync(string name, NetConfig config, CancellationToken ct);
    /// <summary>Add a firewall rule.</summary>
    Task AddFirewallRuleAsync(FirewallRule rule, CancellationToken ct);
    /// <summary>Remove a firewall rule.</summary>
    Task RemoveFirewallRuleAsync(string ruleId, CancellationToken ct);
}
