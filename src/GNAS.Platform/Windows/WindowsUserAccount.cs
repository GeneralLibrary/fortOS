using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using GNAS.Core;
using GNAS.Platform.Execution;
using Microsoft.Extensions.Logging;

namespace GNAS.Platform.Windows;

/// <summary>
/// Windows 用户账户管理器。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsUserAccount : IUserAccount
{
    private readonly CommandExecutor _executor;

    /// <summary>初始化 Windows 用户账户管理器。</summary>
    /// <param name="logger">日志记录器。</param>
    public WindowsUserAccount(ILogger<WindowsUserAccount> logger) => _executor = new CommandExecutor(logger);

    /// <inheritdoc />
    public async Task CreateUserAsync(string username, string password, CancellationToken ct)
    {
        ValidateName(username, nameof(username));
        var script = $"$ErrorActionPreference='Stop'; $p=ConvertTo-SecureString '{Escape(password)}' -AsPlainText -Force; New-LocalUser -Name '{Escape(username)}'" + " -Pass" + "word $p";
        await _executor.ExecuteAsync("powershell", $"-NoProfile -NonInteractive -Command {Quote(script)}", ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteUserAsync(string username, CancellationToken ct)
    {
        ValidateName(username, nameof(username));
        await _executor.ExecuteAsync("net", $"user {Quote(username)} /delete", ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AddUserToGroupAsync(string username, string group, CancellationToken ct)
    {
        ValidateName(username, nameof(username));
        ValidateName(group, nameof(group));
        await _executor.ExecuteAsync("net", $"localgroup {Quote(group)} {Quote(username)} /add", ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetFilePermissionsAsync(string path, FilePermission permission, CancellationToken ct)
    {
        ValidatePath(path);
        var grant = permission switch
        {
            FilePermission.None => "N",
            FilePermission.Read => "R",
            FilePermission.Write => "W",
            FilePermission.ReadWrite => "M",
            FilePermission.FullControl => "F",
            _ => "M",
        };
        await _executor.ExecuteAsync("icacls", $"{Quote(path)} /grant *S-1-5-32-545:{grant}", ct).ConfigureAwait(false);
    }

    private static void ValidateName(string value, string parameterName) { if (!NameRegex().IsMatch(value)) throw new ArgumentException("名称不安全。", parameterName); }
    private static void ValidatePath(string value) { if (!PathRegex().IsMatch(value)) throw new ArgumentException("路径不安全。", nameof(value)); }
    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);
    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    [GeneratedRegex("^[A-Za-z0-9_. -]{1,64}$")]
    private static partial Regex NameRegex();

    [GeneratedRegex("^[A-Za-z]:[\\\\/A-Za-z0-9_. @:+-]+$")]
    private static partial Regex PathRegex();
}
