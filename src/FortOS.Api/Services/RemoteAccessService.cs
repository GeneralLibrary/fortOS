using System.Text.Json;
using System.Text.Json.Serialization;
using FortOS.Core;

namespace FortOS.Api.Services;

/// <summary>
/// 远程访问服务(P0-3 免穿透):基于 Tailscale 的零配置远程接入。
/// Tailscale 利用 NAT 打洞 + DERP 中继,无需公网 IP/端口映射即可让手机在任意网络访问 NAS。
/// 通过 <see cref="IProcessManager"/> 执行 tailscale CLI;未安装时返回可安装指引。
/// </summary>
public sealed class RemoteAccessService(IProcessManager process, IConfiguration configuration)
{
    /// <summary>配置键:是否启用远程访问。</summary>
    public const string EnabledKey = "remote:enabled";
    /// <summary>配置键:Tailscale 认证密钥(首次登录用;留空则输出交互登录 URL)。</summary>
    public const string AuthKeyKey = "remote:tailscale_auth_key";
    /// <summary>配置键:设备在 Tailscale 网络中的显示名。</summary>
    public const string HostNameKey = "remote:tailscale_hostname";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>读取当前状态(不修改系统)。</summary>
    public async Task<RemoteStatus> GetStatusAsync(CancellationToken ct)
    {
        var enabled = IsEnabled();
        if (!enabled)
        {
            return new RemoteStatus(false, false, false, null, null, "远程访问未启用(remote:enabled=false)。");
        }

        var installed = await IsTailscaleInstalledAsync(ct).ConfigureAwait(false);
        if (!installed)
        {
            return new RemoteStatus(true, false, false, null, null, "未检测到 tailscale,请先安装(apt install tailscale)。");
        }

        var status = await RunTailscaleAsync("status --json", ct).ConfigureAwait(false);
        if (status is null)
        {
            return new RemoteStatus(true, true, false, null, null, "Tailscale 未登录或状态读取失败。");
        }

        using var doc = JsonDocument.Parse(status);
        var root = doc.RootElement;
        var loggedIn = root.TryGetProperty("BackendState", out var state)
                       && string.Equals(state.GetString(), "Running", StringComparison.OrdinalIgnoreCase);
        var self = root.TryGetProperty("Self", out var selfProp) ? selfProp : default;
        var hostName = self.ValueKind == JsonValueKind.Object && self.TryGetProperty("HostName", out var hn)
            ? hn.GetString()
            : configuration[HostNameKey];
        var ip = self.ValueKind == JsonValueKind.Object && self.TryGetProperty("TailscaleIPs", out var ips)
                 && ips.ValueKind == JsonValueKind.Array && ips.GetArrayLength() > 0
            ? ips[0].GetString()
            : null;
        return new RemoteStatus(true, true, loggedIn, hostName, ip, loggedIn ? "已连接。" : "Tailscale 已安装但未登录。");
    }

    /// <summary>启用远程访问:tailscale up。已登录则直接连接;否则输出登录指引。</summary>
    public async Task<RemoteStatus> EnableAsync(CancellationToken ct)
    {
        var authKey = configuration[AuthKeyKey];
        var hostName = configuration[HostNameKey];
        var args = string.IsNullOrWhiteSpace(authKey)
            ? $"up --hostname={Quote(hostName ?? "fortos")}"
            : $"up --hostname={Quote(hostName ?? "fortos")} --authkey={Quote(authKey)}";
        var result = await process.ExecuteCommandAsync(new ProcessStartConfig
        {
            ExecutablePath = "tailscale",
            Arguments = args,
            TimeoutSeconds = 60,
            ThrowOnNonZeroExit = false,
        }, ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            var message = TrimError(result.Stderr) ?? TrimError(result.Stdout);
            return new RemoteStatus(true, true, false, hostName, null, $"启动失败:{message}");
        }

        return await GetStatusAsync(ct).ConfigureAwait(false);
    }

    /// <summary>禁用远程访问:tailscale down(设备保持注册,可随时再连)。</summary>
    public async Task<RemoteStatus> DisableAsync(CancellationToken ct)
    {
        var result = await process.ExecuteCommandAsync(new ProcessStartConfig
        {
            ExecutablePath = "tailscale",
            Arguments = "down",
            TimeoutSeconds = 30,
            ThrowOnNonZeroExit = false,
        }, ct).ConfigureAwait(false);
        return new RemoteStatus(true, result.ExitCode == 0, false, null, null,
            result.ExitCode == 0 ? "已断开。" : $"断开失败:{TrimError(result.Stderr) ?? result.ExitCode.ToString()}");
    }

    /// <summary>是否已启用(配置开关)。</summary>
    public bool IsEnabled()
        => string.Equals(configuration[EnabledKey], "true", StringComparison.OrdinalIgnoreCase);

    private async Task<bool> IsTailscaleInstalledAsync(CancellationToken ct)
    {
        var result = await process.ExecuteCommandAsync(new ProcessStartConfig
        {
            ExecutablePath = "tailscale",
            Arguments = "version",
            TimeoutSeconds = 10,
            ThrowOnNonZeroExit = false,
        }, ct).ConfigureAwait(false);
        return result.ExitCode == 0;
    }

    private async Task<string?> RunTailscaleAsync(string arguments, CancellationToken ct)
    {
        var result = await process.ExecuteCommandAsync(new ProcessStartConfig
        {
            ExecutablePath = "tailscale",
            Arguments = arguments,
            TimeoutSeconds = 15,
            ThrowOnNonZeroExit = false,
        }, ct).ConfigureAwait(false);
        return result.ExitCode == 0 ? result.Stdout : null;
    }

    private static string? TrimError(string? text)
        => string.IsNullOrWhiteSpace(text) ? null : text.ReplaceLineEndings(" ").Trim();

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}

/// <summary>远程访问状态(服务层契约)。</summary>
public sealed record RemoteStatus(
    bool Enabled,
    bool TailscaleInstalled,
    bool LoggedIn,
    string? HostName,
    string? Ip,
    string? Message);
