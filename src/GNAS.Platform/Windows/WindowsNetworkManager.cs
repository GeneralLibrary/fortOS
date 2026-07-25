using System.Net;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using GNAS.Core;
using GNAS.Platform.Execution;
using Microsoft.Extensions.Logging;

namespace GNAS.Platform.Windows;

/// <summary>
/// Windows 网络管理器。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsNetworkManager : INetworkManager
{
    private readonly CommandExecutor _executor;

    /// <summary>初始化 Windows 网络管理器。</summary>
    /// <param name="logger">日志记录器。</param>
    public WindowsNetworkManager(ILogger<WindowsNetworkManager> logger) => _executor = new CommandExecutor(logger);

    /// <inheritdoc />
    public async Task<IReadOnlyList<NetworkInterfaceInfo>> ListInterfacesAsync(CancellationToken ct)
    {
        var result = await PowerShellAsync("$ErrorActionPreference='Stop'; Get-NetAdapter | Select-Object Name,MacAddress,Status,LinkSpeed | ConvertTo-Json -Depth 4", ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(NormalizeJsonArray(result.Stdout));
        return doc.RootElement.EnumerateArray().Select(e => new NetworkInterfaceInfo
        {
            Name = GetString(e, "Name") ?? string.Empty,
            MacAddress = GetString(e, "MacAddress"),
            Addresses = [],
            IsUp = string.Equals(GetString(e, "Status"), "Up", StringComparison.OrdinalIgnoreCase),
            SpeedMbps = ParseSpeed(GetString(e, "LinkSpeed")),
        }).ToArray();
    }

    /// <inheritdoc />
    public async Task ConfigureInterfaceAsync(string name, NetConfig config, CancellationToken ct)
    {
        ValidateName(name, nameof(name));
        ValidateConfig(config);
        if (config.Dhcp)
        {
            await _executor.ExecuteAsync("netsh", $"interface ip set address name={Quote(name)} source=dhcp", ct).ConfigureAwait(false);
        }
        else
        {
            await _executor.ExecuteAsync("netsh", $"interface ip set address name={Quote(name)} static {config.Address} {config.Gateway}", ct).ConfigureAwait(false);
        }

        foreach (var dns in config.DnsServers)
        {
            await _executor.ExecuteAsync("netsh", $"interface ip add dns name={Quote(name)} {dns}", ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task AddFirewallRuleAsync(FirewallRule rule, CancellationToken ct)
    {
        ValidateRule(rule);
        var action = rule.Action.Equals("allow", StringComparison.OrdinalIgnoreCase) || rule.Action.Equals("accept", StringComparison.OrdinalIgnoreCase) ? "Allow" : "Block";
        var script = $"$ErrorActionPreference='Stop'; New-NetFirewallRule -DisplayName '{Escape(rule.RuleId)}' -Direction Inbound -Action {action} {(rule.Port.HasValue ? $"-LocalPort {rule.Port.Value}" : string.Empty)} {(string.IsNullOrWhiteSpace(rule.Protocol) ? string.Empty : $"-Protocol {rule.Protocol}")} {(string.IsNullOrWhiteSpace(rule.Source) ? string.Empty : $"-RemoteAddress {rule.Source}")}";
        return PowerShellAsync(script, ct);
    }

    /// <inheritdoc />
    public Task RemoveFirewallRuleAsync(string ruleId, CancellationToken ct)
    {
        ValidateName(ruleId, nameof(ruleId));
        return PowerShellAsync($"$ErrorActionPreference='Stop'; Get-NetFirewallRule -DisplayName '{Escape(ruleId)}' | Remove-NetFirewallRule", ct);
    }

    private Task<CommandResult> PowerShellAsync(string script, CancellationToken ct) => _executor.ExecuteAsync("powershell", $"-NoProfile -NonInteractive -Command {Quote(script)}", ct);
    private static string NormalizeJsonArray(string json) => string.IsNullOrWhiteSpace(json) ? "[]" : json.TrimStart().StartsWith('[') ? json : "[" + json + "]";
    private static string? GetString(JsonElement e, string n) => e.TryGetProperty(n, out var p) && p.ValueKind != JsonValueKind.Null ? p.ToString() : null;
    private static long? ParseSpeed(string? text) => null;
    private static void ValidateConfig(NetConfig c)
    {
        if (!string.IsNullOrWhiteSpace(c.Address) && !IPAddress.TryParse(c.Address.Split('/')[0], out _)) throw new ArgumentException("地址无效。", nameof(c));
        if (!string.IsNullOrWhiteSpace(c.Gateway) && !IPAddress.TryParse(c.Gateway, out _)) throw new ArgumentException("网关无效。", nameof(c));
    }
    private static void ValidateRule(FirewallRule r) { ValidateName(r.RuleId, nameof(r)); if (r.Port is < 1 or > 65535) throw new ArgumentException("端口无效。", nameof(r)); }
    private static void ValidateName(string value, string parameterName) { if (!NameRegex().IsMatch(value)) throw new ArgumentException("名称不安全。", parameterName); }
    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);
    private static string Quote(string value) => "\"" + value.Replace("\"", "`\"", StringComparison.Ordinal) + "\"";

    [GeneratedRegex("^[A-Za-z0-9_. @:-]+$")]
    private static partial Regex NameRegex();
}
