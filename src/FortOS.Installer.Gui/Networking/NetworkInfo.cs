using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace FortOS.Installer.Gui.Networking;

/// <summary>
/// Detects the primary IPv4 address shown in the installer status bar.
/// Skips loopback, tunnels, common virtual bridges (docker0, veth*, br-*)
/// and link-local addresses so the displayed management URL is reachable
/// from other machines on the LAN.
/// </summary>
public static class NetworkInfo
{
    /// <summary>Default FortOS API management port.</summary>
    public const int ManagementPort = 5000;

    /// <summary>Primary IPv4 address, or <c>null</c> when none is available.</summary>
    public static string? PrimaryIPv4()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up)
                .Where(ni => ni.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .Where(ni => !IsVirtualInterface(ni.Name))
                .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
                .Select(u => u.Address)
                .FirstOrDefault(a => !IPAddress.IsLoopback(a) && !IsLinkLocal(a))?.ToString();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Management URL the admin can open from another machine, or <c>null</c>.</summary>
    public static string? ManagementUrl()
    {
        var ip = PrimaryIPv4();
        return ip is null ? null : $"http://{ip}:{ManagementPort}";
    }

    internal static bool IsVirtualInterface(string name)
        => name.Equals("lo", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("docker", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("veth", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("br-", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("virbr", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("vnic", StringComparison.OrdinalIgnoreCase);

    internal static bool IsLinkLocal(IPAddress address)
    {
        if (address.IsIPv6LinkLocal)
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
    }
}
