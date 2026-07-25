using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using GNAS.Core;
using GNAS.Platform.Execution;
using Microsoft.Extensions.Logging;

namespace GNAS.Platform.Linux;

/// <summary>
/// Linux 用户账户管理器。
/// </summary>
[SupportedOSPlatform("linux")]
public sealed partial class LinuxUserAccount : IUserAccount
{
    private readonly CommandExecutor _executor;

    /// <summary>初始化 Linux 用户账户管理器。</summary>
    /// <param name="logger">日志记录器。</param>
    public LinuxUserAccount(ILogger<LinuxUserAccount> logger)
    {
        _executor = new CommandExecutor(logger);
    }

    /// <inheritdoc />
    public async Task CreateUserAsync(string username, string password, CancellationToken ct)
    {
        ValidateName(username, nameof(username));
        await _executor.ExecuteAsync("useradd", $"--create-home {Quote(username)}", ct).ConfigureAwait(false);
        await _executor.ExecuteAsync("chpasswd", null, ct, standardInput: $"{username}:{password}\n").ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task DeleteUserAsync(string username, CancellationToken ct)
    {
        ValidateName(username, nameof(username));
        return ExecuteIgnoreAsync("userdel", $"--remove {Quote(username)}", ct);
    }

    /// <inheritdoc />
    public Task AddUserToGroupAsync(string username, string group, CancellationToken ct)
    {
        ValidateName(username, nameof(username));
        ValidateName(group, nameof(group));
        return ExecuteIgnoreAsync("usermod", $"-aG {Quote(group)} {Quote(username)}", ct);
    }

    /// <inheritdoc />
    public async Task SetFilePermissionsAsync(string path, FilePermission permission, CancellationToken ct)
    {
        ValidatePath(path);
        var mode = permission switch
        {
            FilePermission.None => "000",
            FilePermission.Read => "444",
            FilePermission.Write => "222",
            FilePermission.ReadWrite => "664",
            FilePermission.FullControl => "775",
            _ => "664",
        };
        await _executor.ExecuteAsync("chmod", $"{mode} {Quote(path)}", ct).ConfigureAwait(false);
        await _executor.ExecuteAsync("chown", $"root:root {Quote(path)}", ct).ConfigureAwait(false);
    }

    private async Task ExecuteIgnoreAsync(string command, string arguments, CancellationToken ct)
    {
        await _executor.ExecuteAsync(command, arguments, ct).ConfigureAwait(false);
    }

    private static void ValidateName(string value, string parameterName)
    {
        if (!NameRegex().IsMatch(value)) throw new ArgumentException("名称不安全。", parameterName);
    }

    private static void ValidatePath(string path)
    {
        if (!PathRegex().IsMatch(path)) throw new ArgumentException("路径不安全。", nameof(path));
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    [GeneratedRegex("^[a-z_][a-z0-9_-]{0,31}$", RegexOptions.IgnoreCase)]
    private static partial Regex NameRegex();

    [GeneratedRegex("^/[A-Za-z0-9_./@:+-]+$")]
    private static partial Regex PathRegex();
}
