using System.Net;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GNAS.Core;
using GNAS.Platform.Execution;
using Microsoft.Extensions.Logging;

namespace GNAS.Platform.Linux;

/// <summary>
/// Linux 网络管理器。
/// </summary>
[SupportedOSPlatform("linux")]
public sealed partial class LinuxNetworkManager : INetworkManager
{
    private readonly CommandExecutor _executor;

    /// <summary>初始化 Linux 网络管理器。</summary>
    /// <param name="logger">日志记录器。</param>
    public LinuxNetworkManager(ILogger<LinuxNetworkManager> logger)
    {
        _executor = new CommandExecutor(logger);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NetworkInterfaceInfo>> ListInterfacesAsync(CancellationToken ct)
    {
        var result = await _executor.ExecuteAsync("ip", "--json addr", ct).ConfigureAwait(false);
        using var document = JsonDocument.Parse(result.Stdout);
        var list = new List<NetworkInterfaceInfo>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var name = GetString(item, "ifname") ?? string.Empty;
            var addresses = new List<string>();
            if (item.TryGetProperty("addr_info", out var addrInfo))
            {
                foreach (var address in addrInfo.EnumerateArray())
                {
                    var local = GetString(address, "local");
                    if (!string.IsNullOrWhiteSpace(local))
                    {
                        addresses.Add(local);
                    }
                }
            }

            list.Add(new NetworkInterfaceInfo
            {
                Name = name,
                MacAddress = GetString(item, "address"),
                Addresses = addresses.ToArray(),
                IsUp = (GetString(item, "operstate") ?? string.Empty).Equals("UP", StringComparison.OrdinalIgnoreCase),
                SpeedMbps = TryReadSpeed(name),
            });
        }

        return list;
    }

    /// <inheritdoc />
    public async Task ConfigureInterfaceAsync(string name, NetConfig config, CancellationToken ct)
    {
        ValidateInterfaceName(name);
        ValidateNetConfig(config);
        Directory.CreateDirectory("/etc/netplan");
        var path = $"/etc/netplan/99-gnas-{name}.yaml";
        var yaml = new StringBuilder();
        yaml.AppendLine("network:");
        yaml.AppendLine("  version: 2");
        yaml.AppendLine("  renderer: NetworkManager");
        yaml.AppendLine("  ethernets:");
        yaml.AppendLine($"    {name}:");
        yaml.AppendLine($"      dhcp4: {config.Dhcp.ToString().ToLowerInvariant()}");
        if (!config.Dhcp && !string.IsNullOrWhiteSpace(config.Address))
        {
            yaml.AppendLine("      addresses:");
            yaml.AppendLine($"        - {config.Address}");
            if (!string.IsNullOrWhiteSpace(config.Gateway))
            {
                yaml.AppendLine("      routes:");
                yaml.AppendLine($"        - to: default");
                yaml.AppendLine($"          via: {config.Gateway}");
            }
        }

        if (config.DnsServers.Length > 0)
        {
            yaml.AppendLine("      nameservers:");
            yaml.AppendLine($"        addresses: [{string.Join(", ", config.DnsServers)}]");
        }

        await File.WriteAllTextAsync(path, yaml.ToString(), ct).ConfigureAwait(false);
        await _executor.ExecuteAsync("netplan", "apply", ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AddFirewallRuleAsync(FirewallRule rule, CancellationToken ct)
    {
        ValidateRule(rule);
        var action = NormalizeAction(rule.Action);
        var protocol = string.IsNullOrWhiteSpace(rule.Protocol) ? "tcp" : rule.Protocol.ToLowerInvariant();
        var nftArgs = $"add rule inet filter input {BuildNftMatch(rule, protocol)} {action} comment {Quote(rule.RuleId)}";
        var nft = await _executor.ExecuteAsync("nft", nftArgs, ct, throwOnNonZeroExit: false).ConfigureAwait(false);
        if (nft.ExitCode != 0)
        {
            var iptablesAction = action == "accept" ? "ACCEPT" : "DROP";
            await _executor.ExecuteAsync("iptables", $"-A INPUT {BuildIptablesMatch(rule, protocol)} -j {iptablesAction} -m comment --comment {Quote(rule.RuleId)}", ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task RemoveFirewallRuleAsync(string ruleId, CancellationToken ct)
    {
        ValidateRuleId(ruleId);
        var nft = await _executor.ExecuteAsync("nft", $"--handle list chain inet filter input", ct, throwOnNonZeroExit: false).ConfigureAwait(false);
        if (nft.ExitCode == 0)
        {
            foreach (var line in nft.Stdout.Split('\n'))
            {
                if (line.Contains($"comment \"{ruleId}\"", StringComparison.Ordinal) && line.Contains("handle", StringComparison.Ordinal))
                {
                    var handle = line[(line.LastIndexOf("handle", StringComparison.Ordinal) + 6)..].Trim();
                    if (int.TryParse(handle, out _))
                    {
                        await _executor.ExecuteAsync("nft", $"delete rule inet filter input handle {handle}", ct, throwOnNonZeroExit: false).ConfigureAwait(false);
                    }
                }
            }
        }

        await _executor.ExecuteAsync("iptables", $"-D INPUT -m comment --comment {Quote(ruleId)} -j ACCEPT", ct, throwOnNonZeroExit: false).ConfigureAwait(false);
        await _executor.ExecuteAsync("iptables", $"-D INPUT -m comment --comment {Quote(ruleId)} -j DROP", ct, throwOnNonZeroExit: false).ConfigureAwait(false);
    }

    private static string BuildNftMatch(FirewallRule rule, string protocol)
    {
        var source = string.IsNullOrWhiteSpace(rule.Source) ? string.Empty : $"ip saddr {rule.Source} ";
        var port = rule.Port.HasValue ? $"{protocol} dport {rule.Port.Value} " : string.Empty;
        return source + port;
    }

    private static string BuildIptablesMatch(FirewallRule rule, string protocol)
    {
        var source = string.IsNullOrWhiteSpace(rule.Source) ? string.Empty : $"-s {rule.Source} ";
        var port = rule.Port.HasValue ? $"-p {protocol} --dport {rule.Port.Value} " : string.Empty;
        return source + port;
    }

    private static long? TryReadSpeed(string name)
    {
        try
        {
            var text = File.ReadAllText($"/sys/class/net/{name}/speed").Trim();
            return long.TryParse(text, out var speed) ? speed : null;
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeAction(string action)
        => action.Equals("allow", StringComparison.OrdinalIgnoreCase) || action.Equals("accept", StringComparison.OrdinalIgnoreCase) ? "accept" : "drop";

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var property) && property.ValueKind != JsonValueKind.Null ? property.ToString() : null;

    private static void ValidateNetConfig(NetConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.Address) && !IsCidr(config.Address)) throw new ArgumentException("IP 地址无效。", nameof(config));
        if (!string.IsNullOrWhiteSpace(config.Gateway) && !IPAddress.TryParse(config.Gateway, out _)) throw new ArgumentException("网关无效。", nameof(config));
        if (config.DnsServers.Any(d => !IPAddress.TryParse(d, out _))) throw new ArgumentException("DNS 地址无效。", nameof(config));
    }

    private static bool IsCidr(string value)
    {
        var parts = value.Split('/');
        return parts.Length == 2 && IPAddress.TryParse(parts[0], out _) && int.TryParse(parts[1], out var prefix) && prefix is >= 0 and <= 128;
    }

    private static void ValidateRule(FirewallRule rule)
    {
        ValidateRuleId(rule.RuleId);
        if (rule.Port is < 1 or > 65535) throw new ArgumentException("端口无效。", nameof(rule));
        if (!string.IsNullOrWhiteSpace(rule.Protocol) && rule.Protocol is not ("tcp" or "udp")) throw new ArgumentException("协议无效。", nameof(rule));
        if (!string.IsNullOrWhiteSpace(rule.Source) && !IsCidr(rule.Source) && !IPAddress.TryParse(rule.Source, out _)) throw new ArgumentException("来源地址无效。", nameof(rule));
    }

    private static void ValidateInterfaceName(string name)
    {
        if (!SafeNameRegex().IsMatch(name)) throw new ArgumentException("接口名称不安全。", nameof(name));
    }

    private static void ValidateRuleId(string ruleId)
    {
        if (!SafeNameRegex().IsMatch(ruleId)) throw new ArgumentException("规则标识不安全。", nameof(ruleId));
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    [GeneratedRegex("^[A-Za-z0-9_.:-]+$")]
    private static partial Regex SafeNameRegex();
}
