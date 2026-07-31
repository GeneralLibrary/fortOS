using System.Text;

namespace FortOS.Modules.Network.Services;

/// <summary>dnsmasq DHCP configuration generator.</summary>
public sealed class DhcpConfigGenerator
{
    /// <summary>Generate dnsmasq configuration.</summary>
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
            throw new ArgumentException("Configuration value cannot contain newlines.", nameof(value));
        }
    }
}
