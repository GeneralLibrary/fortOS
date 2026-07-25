using System.Text.RegularExpressions;
using GNAS.Core;
using Microsoft.Extensions.Logging;

namespace GNAS.Modules.Share.Services;

/// <summary>
/// Samba 用户供给器。
/// 将 GNAS 内部用户桥接到 Linux 系统用户与 Samba 用户数据库（smbpasswd），
/// 使 SMB 客户端可以直接使用 GNAS 账户凭据访问共享。
/// 所有操作均为幂等；在非 Linux 平台或缺少 Samba 工具链时静默降级为空操作。
/// </summary>
public sealed partial class SambaUserProvisioner : ISystemUserProvisioner
{
    private const string SmbPasswdPath = "/usr/bin/smbpasswd";
    private const string GetentPath = "/usr/bin/getent";

    private readonly IUserAccount _userAccount;
    private readonly IProcessManager _processManager;
    private readonly ILogger<SambaUserProvisioner> _logger;

    /// <summary>初始化 Samba 用户供给器。</summary>
    /// <param name="userAccount">系统用户账户管理器。</param>
    /// <param name="processManager">进程管理器。</param>
    /// <param name="logger">日志记录器。</param>
    public SambaUserProvisioner(IUserAccount userAccount, IProcessManager processManager, ILogger<SambaUserProvisioner> logger)
    {
        _userAccount = userAccount;
        _processManager = processManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task ProvisionAsync(string username, string password, CancellationToken ct)
    {
        if (!CanProvision())
        {
            _logger.LogDebug("当前环境不支持 Samba 用户供给，跳过用户 {Username}。", username);
            return;
        }

        ValidateUsername(username);

        // 第一步：确保存在同名 Linux 系统用户，Samba security = user 模式要求系统用户先行存在。
        if (!await SystemUserExistsAsync(username, ct).ConfigureAwait(false))
        {
            await _userAccount.CreateUserAsync(username, password, ct).ConfigureAwait(false);
        }

        // 第二步：写入 Samba 用户数据库。-a 表示不存在时创建、存在时更新密码；
        // -s 从标准输入读取密码，避免密码出现在进程命令行参数中被泄露。
        await _processManager.ExecuteCommandAsync(new ProcessStartConfig
        {
            ExecutablePath = SmbPasswdPath,
            Arguments = $"-s -a {username}",
            StandardInput = $"{password}\n{password}\n",
        }, ct).ConfigureAwait(false);
        _logger.LogInformation("已为用户 {Username} 供给 Samba 账户。", username);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string username, CancellationToken ct)
    {
        if (!CanProvision())
        {
            return;
        }

        ValidateUsername(username);

        // 仅移除 Samba 数据库中的账户；有意保留 Linux 系统用户及其家目录，
        // 避免删除 GNAS 账户时连带销毁用户在共享目录之外的数据。
        try
        {
            await _processManager.ExecuteCommandAsync(new ProcessStartConfig
            {
                ExecutablePath = SmbPasswdPath,
                Arguments = $"-x {username}",
            }, ct).ConfigureAwait(false);
            _logger.LogInformation("已移除用户 {Username} 的 Samba 账户。", username);
        }
        catch (CommandExecutionException ex)
        {
            // 账户本就不存在时 smbpasswd 返回非零，视为幂等成功。
            _logger.LogDebug(ex, "移除用户 {Username} 的 Samba 账户时命令返回非零，视为已移除。", username);
        }
    }

    /// <summary>判断当前主机是否具备 Samba 用户供给条件。</summary>
    private static bool CanProvision() => OperatingSystem.IsLinux() && File.Exists(SmbPasswdPath);

    /// <summary>检查同名 Linux 系统用户是否已存在。</summary>
    private async Task<bool> SystemUserExistsAsync(string username, CancellationToken ct)
    {
        try
        {
            await _processManager.ExecuteCommandAsync(new ProcessStartConfig
            {
                ExecutablePath = GetentPath,
                Arguments = $"passwd {username}",
            }, ct).ConfigureAwait(false);
            return true;
        }
        catch (CommandExecutionException)
        {
            return false;
        }
    }

    /// <summary>校验用户名，防止拼接进命令行参数时发生注入。</summary>
    private static void ValidateUsername(string username)
    {
        if (!UsernameRegex().IsMatch(username))
        {
            throw new ArgumentException("用户名格式不安全，无法用于系统用户供给。", nameof(username));
        }
    }

    [GeneratedRegex("^[a-z_][a-z0-9_-]{0,31}$")]
    private static partial Regex UsernameRegex();
}
