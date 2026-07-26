using GNAS.Core;
using GNAS.Modules.Host;

namespace GNAS.Modules.Network;

/// <summary>Network business module, encapsulating network interfaces, firewall, and network auxiliary configuration.</summary>
public sealed class NetworkModule : NasModuleBase
{
    /// <inheritdoc />
    public override string ModuleId => "network";

    /// <inheritdoc />
    public override string DisplayName => "Network Management";

    /// <inheritdoc />
    public override IReadOnlyList<string> RequiredCapabilities => ["network:interface:read", "network:interface:write", "network:firewall:write"];

    /// <summary>List network interfaces.</summary>
    public Task<IReadOnlyList<NetworkInterfaceInfo>> ListInterfacesAsync(CancellationToken ct) => RequiredService<INetworkManager>().ListInterfacesAsync(ct);

    /// <summary>Configure network interface.</summary>
    public async Task ConfigureInterfaceAsync(string name, NetConfig config, CancellationToken ct)
    {
        ValidateName(name);
        await RequiredService<INetworkManager>().ConfigureInterfaceAsync(name, config, ct).ConfigureAwait(false);
        await PublishAsync("network.interface.changed", "network.interface.changed", new { name, config.Dhcp, config.Address }, ct).ConfigureAwait(false);
    }

    /// <summary>Add firewall rule.</summary>
    public async Task AddFirewallRuleAsync(FirewallRule rule, CancellationToken ct)
    {
        ValidateName(rule.RuleId);
        await RequiredService<INetworkManager>().AddFirewallRuleAsync(rule, ct).ConfigureAwait(false);
        await PublishAsync("network.firewall.changed", "network.firewall.added", rule, ct).ConfigureAwait(false);
    }

    /// <summary>Remove firewall rule.</summary>
    public async Task RemoveFirewallRuleAsync(string ruleId, CancellationToken ct)
    {
        ValidateName(ruleId);
        await RequiredService<INetworkManager>().RemoveFirewallRuleAsync(ruleId, ct).ConfigureAwait(false);
        await PublishAsync("network.firewall.changed", "network.firewall.removed", new { ruleId }, ct).ConfigureAwait(false);
    }

    private static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Contains('\n') || name.Contains('\r') || name.Contains(';'))
        {
            throw new ArgumentException("Name must not contain control characters or command separators.", nameof(name));
        }
    }
}
