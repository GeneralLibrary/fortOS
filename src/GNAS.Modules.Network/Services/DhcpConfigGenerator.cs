using System.Text;

namespace GNAS.Modules.Network.Services;

/// <summary>dnsmasq DHCP 配置生成器。</summary>
public sealed class DhcpConfigGenerator
{
    /// <summary>生成 dnsmasq 配置。</summary>
    public string Generate(string interfaceName, string rangeStart, string rangeEnd, string leaseTime = "12h")
    {
        Validate(interfaceName);
        Validate(rangeStart);
        Validate(rangeEnd);
        Validate(leaseTime);
        var sb = new StringBuilder();
        sb.AppendLine($"interface={interfaceName}");
        sb.AppendLine("bind-interfaces");
        sb.AppendLine($"dhcp-range={rangeStart},{rangeEnd},{leaseTime}");
        return sb.ToString();
    }

    private static void Validate(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Contains('\n') || value.Contains('\r'))
        {
            throw new ArgumentException("配置值不能包含换行。", nameof(value));
        }
    }
}
