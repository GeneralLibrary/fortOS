using GNAS.Core;
using GNAS.Modules.Host;

namespace GNAS.Modules.Network;

/// <summary>网络业务模块，封装网卡、防火墙与网络辅助配置。</summary>
public sealed class NetworkModule : NasModuleBase
{
    /// <inheritdoc />
    public override string ModuleId => "network";

    /// <inheritdoc />
    public override string DisplayName => "网络管理";

    /// <inheritdoc />
    public override IReadOnlyList<string> RequiredCapabilities => ["network:interface:read", "network:interface:write", "network:firewall:write"];

    /// <summary>列出网络接口。</summary>
    public Task<IReadOnlyList<NetworkInterfaceInfo>> ListInterfacesAsync(CancellationToken ct) => RequiredService<INetworkManager>().ListInterfacesAsync(ct);

    /// <summary>配置网络接口。</summary>
    public async Task ConfigureInterfaceAsync(string name, NetConfig config, CancellationToken ct)
    {
        ValidateName(name);
        await RequiredService<INetworkManager>().ConfigureInterfaceAsync(name, config, ct).ConfigureAwait(false);
        await PublishAsync("network.interface.changed", "network.interface.changed", new { name, config.Dhcp, config.Address }, ct).ConfigureAwait(false);
    }

    /// <summary>添加防火墙规则。</summary>
    public async Task AddFirewallRuleAsync(FirewallRule rule, CancellationToken ct)
    {
        ValidateName(rule.RuleId);
        await RequiredService<INetworkManager>().AddFirewallRuleAsync(rule, ct).ConfigureAwait(false);
        await PublishAsync("network.firewall.changed", "network.firewall.added", rule, ct).ConfigureAwait(false);
    }

    /// <summary>删除防火墙规则。</summary>
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
            throw new ArgumentException("名称不能包含控制字符或命令分隔符。", nameof(name));
        }
    }
}
