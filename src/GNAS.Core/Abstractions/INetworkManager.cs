namespace GNAS.Core;

/// <summary>网络管理抽象。</summary>
public interface INetworkManager
{
    /// <summary>列出网络接口。</summary>
    Task<IReadOnlyList<NetworkInterfaceInfo>> ListInterfacesAsync(CancellationToken ct);
    /// <summary>配置网络接口。</summary>
    Task ConfigureInterfaceAsync(string name, NetConfig config, CancellationToken ct);
    /// <summary>添加防火墙规则。</summary>
    Task AddFirewallRuleAsync(FirewallRule rule, CancellationToken ct);
    /// <summary>删除防火墙规则。</summary>
    Task RemoveFirewallRuleAsync(string ruleId, CancellationToken ct);
}
