using System.Text.RegularExpressions;
using GORT.Core;
using Microsoft.Extensions.Logging;

namespace GORT.Modules.Share.Services;

/// <summary>
/// Samba user provisioner.
/// Bridges GORT internal users to Linux system users and the Samba user database (smbpasswd),
/// allowing SMB clients to access shares directly using GORT account credentials.
/// All operations are idempotent; silently degrades to a no-op on non-Linux platforms or
/// when the Samba toolchain is missing.
/// </summary>
public sealed partial class SambaUserProvisioner : ISystemUserProvisioner
{
    private const string SmbPasswdPath = "/usr/bin/smbpasswd";
    private const string GetentPath = "/usr/bin/getent";

    private readonly IUserAccount _userAccount;
    private readonly IProcessManager _processManager;
    private readonly ILogger<SambaUserProvisioner> _logger;

    /// <summary>Initialize the Samba user provisioner.</summary>
    /// <param name="userAccount">System user account manager.</param>
    /// <param name="processManager">Process manager.</param>
    /// <param name="logger">Logger.</param>
    public SambaUserProvisioner(IUserAccount userAccount, IProcessManager processManager, ILogger<SambaUserProvisioner> logger)
    {
        _userAccount = userAccount;
        _processManager = processManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task ProvisionAsync(string username, string password, CancellationToken ct)
    {
        // First validate the username format to ensure safe command construction and log output.
        ValidateUsername(username);

        if (!CanProvision())
        {
            _logger.LogDebug("Current environment does not support Samba user provisioning; skipping user {Username}.", username);
            return;
        }

        // Step 1: Ensure a Linux system user with the same name exists. Samba security = user mode requires the system user to exist first.
        if (!await SystemUserExistsAsync(username, ct).ConfigureAwait(false))
        {
            await _userAccount.CreateUserAsync(username, password, ct).ConfigureAwait(false);
        }

        // Step 2: Write to the Samba user database. -a means create if not exists, update password if exists;
        // -s reads the password from standard input to avoid leaking it through process command-line arguments.
        await _processManager.ExecuteCommandAsync(new ProcessStartConfig
        {
            ExecutablePath = SmbPasswdPath,
            Arguments = $"-s -a {username}",
            StandardInput = $"{password}\n{password}\n",
        }, ct).ConfigureAwait(false);
        _logger.LogInformation("Samba account provisioned for user {Username}.", username);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string username, CancellationToken ct)
    {
        // First validate the username format to ensure safe command construction and log output.
        ValidateUsername(username);

        if (!CanProvision())
        {
            return;
        }

        // Only remove the account from the Samba database; intentionally keep the Linux system user and home directory
        // to avoid destroying user data outside the share directories when deleting a GORT account.
        try
        {
            await _processManager.ExecuteCommandAsync(new ProcessStartConfig
            {
                ExecutablePath = SmbPasswdPath,
                Arguments = $"-x {username}",
            }, ct).ConfigureAwait(false);
            _logger.LogInformation("Samba account removed for user {Username}.", username);
        }
        catch (CommandExecutionException ex)
        {
            // smbpasswd returns non-zero when the account does not already exist; treat as idempotent success.
            _logger.LogDebug(ex, "Command returned non-zero when removing Samba account for user {Username}; treating as already removed.", username);
        }
    }

    /// <summary>Determines whether the current host can provision Samba users.</summary>
    private static bool CanProvision() => OperatingSystem.IsLinux() && File.Exists(SmbPasswdPath);

    /// <summary>Checks whether a Linux system user with the same name already exists.</summary>
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

    /// <summary>Validates the username to prevent injection when concatenating into command-line arguments.</summary>
    private static void ValidateUsername(string username)
    {
        if (!UsernameRegex().IsMatch(username))
        {
            throw new ArgumentException("Username format is unsafe and cannot be used for system user provisioning.", nameof(username));
        }
    }

    [GeneratedRegex("^[a-z_][a-z0-9_-]{0,31}$")]
    private static partial Regex UsernameRegex();
}
