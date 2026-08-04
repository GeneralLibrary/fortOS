using System.Text;

namespace FortOS.Installer.Core.Tools;

/// <summary>无线网络条目(来自 nmcli 扫描)。</summary>
public sealed record WifiNetwork(string Ssid, string Signal, string Security);

/// <summary>
/// NetworkManager(nmcli)适配器:扫描与连接无线网络。
/// 扫描失败(无无线网卡、NetworkManager 未运行)时返回空列表,不抛出。
/// </summary>
public class NmcliTool
{
    private readonly IProcessRunner _runner;

    public NmcliTool(IProcessRunner runner) => _runner = runner;

    /// <summary>扫描可用无线网络。失败返回空列表。</summary>
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
                // nmcli -t 字段以 ':' 分隔,SSID 内的 ':' 被转义为 '\:'、'\' 被转义为 '\\'。
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
    /// 连接无线网络。成功返回 (true, null);失败返回 (false, 错误摘要)。
    /// 注:密码以 argv 传入 nmcli(单用户 live 安装器可接受,45s 超时窗口短)。
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

    /// <summary>按 nmcli -t 转义规则拆分字段:':' 分隔、'\:' 还原为 ':'、'\\' 还原为 '\'。</summary>
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
