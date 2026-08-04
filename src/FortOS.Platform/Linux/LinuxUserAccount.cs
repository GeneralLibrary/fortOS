using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using FortOS.Core;
using FortOS.Platform.Execution;
using Microsoft.Extensions.Logging;

namespace FortOS.Platform.Linux;

/// <summary>
/// Linux user account manager.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed partial class LinuxUserAccount : IUserAccount
{
    private readonly CommandExecutor _executor;

    /// <summary>Initializes the Linux user account manager.</summary>
    /// <param name="logger">Logger.</param>
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
        return _executor.ExecuteAsync("userdel", $"--remove {Quote(username)}", ct);
    }

    /// <inheritdoc />
    public Task AddUserToGroupAsync(string username, string group, CancellationToken ct)
    {
        ValidateName(username, nameof(username));
        ValidateName(group, nameof(group));
        return _executor.ExecuteAsync("usermod", $"-aG {Quote(group)} {Quote(username)}", ct);
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
            // Full control means every principal may read, write and execute; 775 would deny
            // writes to non-root users once the file is chowned to root:root below.
            FilePermission.FullControl => "777",
            _ => "664",
        };
        await _executor.ExecuteAsync("chmod", $"{mode} {Quote(path)}", ct).ConfigureAwait(false);
        await _executor.ExecuteAsync("chown", $"root:root {Quote(path)}", ct).ConfigureAwait(false);
    }

    private static void ValidateName(string value, string parameterName)
    {
        if (!NameRegex().IsMatch(value)) throw new ArgumentException("Unsafe name.", parameterName);
    }

    private static void ValidatePath(string path)
    {
        if (!PathRegex().IsMatch(path)) throw new ArgumentException("Unsafe path.", nameof(path));
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    [GeneratedRegex("^[a-z_][a-z0-9_-]{0,31}$", RegexOptions.IgnoreCase)]
    private static partial Regex NameRegex();

    [GeneratedRegex("^/[A-Za-z0-9_./@:+-]+$")]
    private static partial Regex PathRegex();
}
