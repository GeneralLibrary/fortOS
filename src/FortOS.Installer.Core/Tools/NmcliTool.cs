using System.Text;

namespace FortOS.Installer.Core.Tools;

/// <summary>Wireless network entry (from an nmcli scan).</summary>
public sealed record WifiNetwork(string Ssid, string Signal, string Security);

/// <summary>
/// NetworkManager (nmcli) adapter: scans for and connects to wireless networks.
/// Returns an empty list when the scan fails (no wireless adapter, NetworkManager not running), without throwing.
/// </summary>
public class NmcliTool
{
    private readonly IProcessRunner _runner;

    public NmcliTool(IProcessRunner runner) => _runner = runner;

    /// <summary>Scans for available wireless networks. Returns an empty list on failure.</summary>
    public async Task<IReadOnlyList<WifiNetwork>> ScanAsync(CancellationToken ct)
    {
        try
        {
            var result = await _runner.RunAsync(
                "nmcli",
                ["-t", "-f", "SSID,SIGNAL,SECURITY", "dev", "wifi", "list", "--rescan", "yes"],
                ct,
                timeout: TimeSpan.FromSeconds(20),
                throwOnNonZeroExit: false).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                return [];
            }

            var networks = new List<WifiNetwork>();
            foreach (var line in result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                // nmcli -t fields are separated by ':'; a ':' inside an SSID is escaped as '\:' and '\' is escaped as '\\'.
                var parts = SplitFields(line);
                if (parts.Length < 2)
                {
                    continue;
                }

                var ssid = parts[0];
                if (string.IsNullOrWhiteSpace(ssid))
                {
                    continue;
                }

                networks.Add(new WifiNetwork(ssid, parts[1], parts.Length > 2 ? parts[2] : string.Empty));
            }

            return networks;
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Connects to a wireless network. Returns (true, null) on success; (false, error summary) on failure.
    /// Note: the password is passed to nmcli via argv (acceptable for a single-user live installer; the 45s timeout window is short).
    /// </summary>
    public async Task<(bool Ok, string? Error)> ConnectAsync(string ssid, string? password, CancellationToken ct)
    {
        var args = new List<string> { "dev", "wifi", "connect", ssid };
        if (!string.IsNullOrEmpty(password))
        {
            args.Add("password");
            args.Add(password);
        }

        try
        {
            var result = await _runner.RunAsync(
                "nmcli",
                args,
                ct,
                timeout: TimeSpan.FromSeconds(45),
                throwOnNonZeroExit: false).ConfigureAwait(false);
            if (result.ExitCode == 0)
            {
                return (true, null);
            }

            var detail = string.IsNullOrWhiteSpace(result.Stderr) ? result.Stdout : result.Stderr;
            return (false, detail.Trim());
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>Splits fields according to the nmcli -t escaping rules: ':' separates, '\:' becomes ':', '\\' becomes '\'.</summary>
    private static string[] SplitFields(string line)
    {
        var fields = new List<string>();
        var buffer = new StringBuilder();
        var escaped = false;
        foreach (var ch in line)
        {
            if (escaped)
            {
                buffer.Append(ch);
                escaped = false;
            }
            else if (ch == '\\')
            {
                escaped = true;
            }
            else if (ch == ':')
            {
                fields.Add(buffer.ToString());
                buffer.Clear();
            }
            else
            {
                buffer.Append(ch);
            }
        }

        fields.Add(buffer.ToString());
        return fields.ToArray();
    }
}
