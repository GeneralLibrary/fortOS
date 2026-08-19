using FortOS.Installer.Core.Models;
using FortOS.Installer.Core.Session;
using FortOS.Installer.Core.Tools;

namespace FortOS.Installer.Core.Steps;

/// <summary>
/// Chroot target system configuration step (design doc 5.4): fstab, hostname,
/// timezone/locale/keyboard, admin account, service enabling, fortos.env,
/// network, and cleanup of live residue.
/// </summary>
public sealed class ChrootStep : IInstallStep
{
    /// <summary>Service enable list consistent with eng/iso/config/hooks/live/0100-fortos-runtime.hook.chroot (including the optional nut-monitor).</summary>
    private static readonly string[] EnabledServices =
    [
        "docker.service", "containerd.service", "smbd.service", "nmbd.service",
        "nfs-server.service", "nfs-mountd.service", "rpcbind.service",
        "vsftpd.service", "ssh.service", "NetworkManager.service", "fortos.service",
        "nut-monitor.service", "fortos-banner.service",
    ];

    private readonly ChrootRunner _chroot;

    public ChrootStep(ChrootRunner chroot) => _chroot = chroot;

    public string Name => "Configure";

    public InstallerPhase Phase => InstallerPhase.Configuring;

    public async Task ExecuteAsync(InstallContext context, CancellationToken ct)
    {
        var target = context.TargetMount;
        var config = context.Config;

        await _chroot.BindMountsAsync(target, ct).ConfigureAwait(false);

        WriteFstab(context, target);
        WriteCrypttab(context, target);
        WriteHostname(context, target);
        WriteTimezone(context, target);
        WriteLocale(context, target);
        WriteKeyboard(context, target);
        WriteFortosEnv(context, target);

        await ConfigureUserAsync(context, target, ct).ConfigureAwait(false);
        // Generate the wizard-selected locale inside the target chroot (see
        // WriteLocale → /etc/locale.gen). Doing it explicitly here, instead of
        // inheriting whatever the live environment generated at boot, keeps the
        // installed system's locale set deterministic and correct.
        await GenerateLocalesAsync(target, ct).ConfigureAwait(false);
        // Write the account password set in the wizard into the FortOS user database
        // (/srv/nas/database/nas.db), so the Web admin UI can log in with the same
        // credentials on first boot without anonymous registration.
        await SeedFortosUserAsync(context, target, ct).ConfigureAwait(false);
        await EnableServicesAsync(target, ct).ConfigureAwait(false);
        await ConfigureNetworkAsync(context, target, ct).ConfigureAwait(false);
        await ConfigureRaidAsync(context, target, ct).ConfigureAwait(false);
        // Rebuild initramfs unconditionally (not only in RAID mode): the target
        // system must boot with a standard initramfs; the initrd left over from the
        // live environment is specific to live-boot booting, and after being copied
        // to the target disk it may be missing, corrupted, or lack the target disk
        // controller/rootfs drivers — when grub-mkconfig cannot find an initrd it
        // silently generates a grub.cfg without an initrd line, and the kernel panics
        // on reboot with "VFS: Unable to mount root fs on unknown-block(0,0)".
        await RebuildInitramfsAsync(target, ct).ConfigureAwait(false);
        await CleanupLiveResidueAsync(target, ct).ConfigureAwait(false);

        context.Summary.Hostname = SanitizeHostname(config.Network.Hostname); // record the actual value written
        context.Summary.Username = config.Account.Username;
        context.Summary.Language = config.Locale.Language;
        context.Summary.Timezone = config.Account.Timezone;
    }

    // ---------------------------------------------------------------------
    // File writing (operates directly on /target, unit-testable)
    // ---------------------------------------------------------------------

    private static void WriteFstab(InstallContext context, string target)
        => WriteFile(target, "etc/fstab", BuildFstab(context));

    private static void WriteCrypttab(InstallContext context, string target)
    {
        var crypttab = BuildCrypttab(context);
        if (!string.IsNullOrEmpty(crypttab))
        {
            WriteFile(target, "etc/crypttab", crypttab);
        }
    }

    /// <summary>Generate /etc/fstab content (pure function, unit-testable).</summary>
    internal static string BuildFstab(InstallContext context)
    {
        var rootFs = context.Config.RootFs == RootFileSystem.Btrfs ? "btrfs" : "ext4";
        // A missing root UUID means blkid collection failed; error out rather than generate a broken fstab.
        if (!context.Uuids.TryGetValue("root", out var rootUuid))
        {
            throw new Exceptions.StepException("Configure", "Root partition UUID was not collected — cannot build /etc/fstab.");
        }

        var lines = new List<string>
        {
            $"UUID={rootUuid} / {rootFs} defaults,noatime 0 1",
        };
        if (context.Uuids.TryGetValue("efi", out var efiUuid))
        {
            lines.Add($"UUID={efiUuid} /boot/efi vfat umask=0077 0 1");
        }
        if (context.Uuids.TryGetValue("swap", out var swapUuid))
        {
            lines.Add($"UUID={swapUuid} none swap sw 0 0");
        }
        if (context.Uuids.TryGetValue("data", out var dataUuid))
        {
            var dataFs = context.Config.Data.FileSystem.ToString().ToLowerInvariant();
            // LUKS data disks must be unlocked via crypttab and mounted through the
            // mapper device. If the container UUID is missing (collection failed),
            // fail rather than fall back to direct mounting — that would fail to
            // mount after reboot.
            if (context.Config.Data.Mode == DataDiskMode.Luks)
            {
                if (!context.Uuids.ContainsKey("data-luks"))
                {
                    throw new Exceptions.StepException("Configure", "LUKS container UUID was not collected — cannot write /etc/crypttab.");
                }
                lines.Add($"/dev/mapper/{context.Config.Data.LuksMapperName} /srv/nas {dataFs} defaults,noatime 0 2");
            }
            else
            {
                lines.Add($"UUID={dataUuid} /srv/nas {dataFs} defaults,noatime 0 2");
            }
        }
        return string.Join('\n', lines) + "\n";
    }

    /// <summary>Generate /etc/crypttab content (LUKS data disk; pure function, unit-testable).</summary>
    internal static string BuildCrypttab(InstallContext context)
    {
        if (!context.Uuids.TryGetValue("data-luks", out var luksUuid))
        {
            return string.Empty;
        }
        return $"{context.Config.Data.LuksMapperName} UUID={luksUuid} none luks\n";
    }

    private static void WriteHostname(InstallContext context, string target)
    {
        var hostname = SanitizeHostname(context.Config.Network.Hostname);
        WriteFile(target, "etc/hostname", hostname + "\n");
        WriteFile(target, "etc/hosts", $"127.0.0.1 localhost\n127.0.1.1 {hostname}\n\n::1 localhost ip6-localhost ip6-loopback\n");
    }

    private static void WriteTimezone(InstallContext context, string target)
    {
        var tz = context.Config.Account.Timezone;
        WriteFile(target, "etc/timezone", tz + "\n");
        // Replace an existing old symlink.
        var localtime = Path.Combine(target, "etc/localtime");
        try
        {
            if (File.Exists(localtime))
            {
                File.Delete(localtime);
            }
        }
        catch
        {
            // Failure to delete the symlink is not fatal.
        }
        try
        {
            File.CreateSymbolicLink(localtime, $"/usr/share/zoneinfo/{tz}");
        }
        catch
        {
            // Skip when the target zoneinfo does not exist (the timezone value itself was already written to /etc/timezone).
        }
    }

    private static void WriteLocale(InstallContext context, string target)
    {
        // Normalize to xx_YY.UTF-8 so /etc/default/locale and locale-gen both get
        // a value glibc accepts (e.g. YAML "zh_CN" without the charset suffix);
        // a malformed language would otherwise make locale-gen fail the install.
        var language = NormalizeLanguage(context.Config.Locale.Language);
        WriteFile(target, "etc/default/locale", $"LANG={language}\n");
        // /etc/locale.gen drives the `locale-gen` run in GenerateLocalesAsync.
        // The installer rsyncs the live rootfs verbatim, so its locale-archive
        // only contains what live-config happened to generate at boot; declaring
        // and generating the wizard-selected language explicitly makes the
        // installed system's locale set deterministic.
        WriteFile(target, "etc/locale.gen", BuildLocaleGen(language));
    }

    /// <summary>
    /// Normalize a locale string to the <c>xx_YY.UTF-8</c> form Debian's
    /// locale-gen accepts: keeps the base name and forces the UTF-8 charset
    /// (matching the system's UTF-8 console). Null, empty and character-invalid
    /// values (including unsupported @modifier forms such as sr_RS@latin) fall
    /// back to <c>en_US.UTF-8</c>; this only guarantees character validity, an
    /// exotic-but-legal base may still fail later in locale-gen.
    /// </summary>
    internal static string NormalizeLanguage(string? language)
    {
        var baseName = language ?? string.Empty;
        var dot = baseName.IndexOf('.');
        if (dot > 0)
        {
            baseName = baseName[..dot];
        }
        if (baseName.Length == 0 || !baseName.All(static c => char.IsAsciiLetter(c) || c == '_'))
        {
            return "en_US.UTF-8";
        }
        return baseName + ".UTF-8";
    }

    /// <summary>Generate /etc/locale.gen content (pure function, unit-testable). en_US.UTF-8 is always kept as a fallback.</summary>
    internal static string BuildLocaleGen(string language)
    {
        var lines = new List<string> { "en_US.UTF-8 UTF-8" };
        if (!string.Equals(language, "en_US.UTF-8", StringComparison.OrdinalIgnoreCase))
        {
            lines.Add($"{language} UTF-8");
        }
        return string.Join('\n', lines) + "\n";
    }

    private static void WriteKeyboard(InstallContext context, string target)
        => WriteFile(target, "etc/default/keyboard", $"XKBLAYOUT=\"{context.Config.Locale.Keyboard}\"\nXKBMODEL=\"pc105\"\n");

    private static void WriteFortosEnv(InstallContext context, string target)
    {
        // Keep consistent with eng/iso/config/includes.chroot/etc/fortos/fortos.env:
        // ASPNETCORE_URLS must explicitly listen on 0.0.0.0, otherwise Kestrel by
        // default listens only on localhost and the admin UI is unreachable from
        // outside the LAN/VM.
        // dashboard__enabled=true: the Web admin UI is enabled by default
        // (appsettings.json defaults to false, which would make /dashboard 404).
        // In environment variables, __ maps to the configuration section separator :.
        WriteFile(
            target,
            "etc/fortos/fortos.env",
            "ASPNETCORE_URLS=http://0.0.0.0:5000\n"
            + "ASPNETCORE_ENVIRONMENT=Production\n"
            + "FortOS_DATA_ROOT=/srv/nas\n"
            + "FortOS_CONFIG_PATH=/srv/nas/config/nas.yaml\n"
            + "DOTNET_EnableDiagnostics=0\n"
            + "dashboard__enabled=true\n");
        var version = ReadLiveFile(target, "etc/fortos/version");
        if (!string.IsNullOrEmpty(version))
        {
            context.Summary.FortosVersion = version.Trim();
        }
    }

    // ---------------------------------------------------------------------
    // Commands inside the chroot
    // ---------------------------------------------------------------------

    private async Task ConfigureUserAsync(InstallContext context, string target, CancellationToken ct)
    {
        var username = context.Config.Account.Username;
        var home = $"/home/{username}";
        // The username is already restricted to a safe subset by ValidateConfig; quotes are still added here for defense in depth.
        var qUsername = ShellQuote(username);
        var qHome = ShellQuote(home);

        // Idempotent creation: skip if the user already exists (retry scenario); report other failures (disk full, etc.) as-is.
        await _chroot.RunScriptAsync(
            target,
            $"id -u '{qUsername}' >/dev/null 2>&1 || useradd -m -d '{qHome}' -s /bin/bash -G sudo {qUsername}",
            ct).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(context.Config.Account.Password))
        {
            // The password is passed to chpasswd via stdin, not on the command line.
            await _chroot.RunScriptAsync(
                target,
                "chpasswd",
                ct,
                standardInput: $"{username}:{context.Config.Account.Password}\n").ConfigureAwait(false);

            // Samba user password: once seeded into the FortOS user database,
            // IdentityService's ProvisionSystemUsersAsync (SambaUserProvisioner)
            // will not trigger, so smbpasswd -a must be run explicitly; otherwise
            // SMB shares fail to authenticate with the same account password. The
            // password goes through stdin.
            await _chroot.RunScriptAsync(
                target,
                $"pdbedit -L 2>/dev/null | grep -q '^{username}' || smbpasswd -s -a {username}",
                ct,
                standardInput: $"{context.Config.Account.Password}\n{context.Config.Account.Password}\n").ConfigureAwait(false);
        }

        // sudoers: FortOS admin gets passwordless sudo.
        WriteFile(target, "etc/sudoers.d/90-fortos-admin", $"{username} ALL=(ALL) NOPASSWD:ALL\n");

        // Autologin: the first boot after install enters the system directly (no
        // account password), a common trade-off for local NAS scenarios. Note the
        // security impact: physical console access yields a shell (admin has
        // passwordless sudo). SSH still requires the account password, unaffected.
        WriteFile(
            target,
            "etc/systemd/system/getty@tty1.service.d/autologin.conf",
            $"[Service]\nExecStart=\nExecStart=-/sbin/agetty --autologin {username} --noclear %I $TERM\n");
        WriteFile(
            target,
            "etc/systemd/system/serial-getty@ttyS0.service.d/autologin.conf",
            $"[Service]\nExecStart=\nExecStart=-/sbin/agetty --autologin {username} --noclear %I $TERM\n");

        if (!string.IsNullOrWhiteSpace(context.Config.Account.SshPublicKey))
        {
            var sshDir = Path.Combine(target, home.TrimStart('/'), ".ssh");
            Directory.CreateDirectory(sshDir);
            WriteFile(target, $"{home.TrimStart('/')}/.ssh/authorized_keys", context.Config.Account.SshPublicKey.TrimEnd() + "\n");
            // Permissions are corrected by chown inside the chroot.
            await _chroot.RunScriptAsync(
                target,
                $"chown -R '{qUsername}':'{qUsername}' '{qHome}/.ssh' && chmod 700 '{qHome}/.ssh' && chmod 600 '{qHome}/.ssh/authorized_keys'",
                ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Generates the locales declared in /etc/locale.gen (written by WriteLocale)
    /// inside the target chroot. The locale-archive is then guaranteed to contain
    /// the wizard-selected language; without this, a missing locale makes tools
    /// report "cannot set locale" and CJK output degrade under LANG=zh_CN.UTF-8.
    /// locale-gen exits non-zero if any declared locale fails — a visible install
    /// failure is preferable to a broken locale at first boot.
    /// </summary>
    private async Task GenerateLocalesAsync(string target, CancellationToken ct)
    {
        await _chroot.RunScriptAsync(target, "locale-gen", ct, timeout: TimeSpan.FromMinutes(2)).ConfigureAwait(false);
    }

    private async Task EnableServicesAsync(string target, CancellationToken ct)
    {
        // Failures enabling the main services must be visible (otherwise the install
        // reports success but none of the target system's services are enabled).
        // nut-monitor is optional (absent when the NAS has no UPS), so it is
        // tolerated separately.
        var enable = string.Join(' ', EnabledServices);
        await _chroot.RunScriptAsync(
            target,
            $"systemctl enable {enable} || {{ echo 'service enable failed' >&2; exit 1; }}",
            ct).ConfigureAwait(false);
    }

    private async Task ConfigureNetworkAsync(InstallContext context, string target, CancellationToken ct)
    {
        var network = context.Config.Network;
        if (network.Mode == NetworkMode.Dhcp)
        {
            // NetworkManager enables DHCP on Ethernet by default; no connection file needs to be written.
            return;
        }

        var address = network.Address ?? throw new Exceptions.ConfigException("Static network mode requires network.address.");
        var dns = network.Dns.Count > 0 ? string.Join(';', network.Dns) : string.Empty;
        var gateway = network.Gateway is null ? string.Empty : $",{network.Gateway}";

        var connection = string.Join('\n',
        $"[connection]",
        "id=fortos-eth0",
        "type=ethernet",
        "interface-name=eth0",
        "autoconnect=true",
        "",
        "[ipv4]",
        "method=manual",
        $"address1={address}{gateway}",
        dns.Length > 0 ? $"dns={dns};" : string.Empty,
        "",
        "[ipv6]",
        "method=auto",
        "");

        WriteFile(target, "etc/NetworkManager/system-connections/fortos-eth0.nmconnection", connection);
        await _chroot.RunScriptAsync(
            target,
            "chmod 600 /etc/NetworkManager/system-connections/fortos-eth0.nmconnection",
            ct).ConfigureAwait(false);
    }

    private async Task ConfigureRaidAsync(InstallContext context, string target, CancellationToken ct)
    {
        if (context.Config.Data.Mode != DataDiskMode.Raid)
        {
            return;
        }
        // Record the array for automatic assembly after reboot (mdadm failures must
        // be visible). Initramfs rebuild is handled uniformly by
        // RebuildInitramfsAsync (write mdadm.conf first, then rebuild, so the RAID
        // configuration goes into the initrd).
        await _chroot.RunScriptAsync(
            target,
            "mdadm --detail --scan > /etc/mdadm/mdadm.conf || { echo 'mdadm scan failed' >&2; exit 1; }",
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Rebuilds a standard initramfs inside the target system's chroot
    /// (update-initramfs -u). The installer rsyncs the live rootfs verbatim to the
    /// target disk, and the initrd in the live environment's /boot is specific to
    /// live-boot booting; it must be regenerated from the target system's own
    /// /lib/modules so the initrd carries the target disk controller and rootfs
    /// drivers and is referenced correctly by grub-mkconfig. A rebuild failure
    /// (missing modules, corrupted initramfs-tools, etc.) must fail the install
    /// with a visible error rather than produce an unbootable system.
    /// </summary>
    private async Task RebuildInitramfsAsync(string target, CancellationToken ct)
    {
        // RunScriptAsync throws ToolException on a non-zero exit code by default — errors are not swallowed.
        await _chroot.RunScriptAsync(
            target,
            "update-initramfs -u",
            ct,
            timeout: TimeSpan.FromMinutes(5)).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes the account password set in the install wizard into the target
    /// system's FortOS user database (/srv/nas/database/nas.db, SQLite). On the
    /// target system's first boot, the Web admin UI and the fortos CLI then log in
    /// directly with the same account password, skipping the anonymous
    /// registration step. The table schema matches the users table in
    /// <c>FortOS.Core/Data/DatabaseProvider.cs</c>, and the first user
    /// automatically gets the admin+user roles (consistent with
    /// IdentityService.CreateLocalUserAsync). A failure must fail the install:
    /// otherwise the install reports success but the Web UI cannot log in with the
    /// agreed credentials.
    /// </summary>
    private async Task SeedFortosUserAsync(InstallContext context, string target, CancellationToken ct)
    {
        var username = context.Config.Account.Username;
        var password = context.Config.Account.Password;
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            // Skip when no account password is configured (e.g. a pure automated
            // flow did not provide one); the BootstrapOnly registration on FortOS
            // first boot covers this.
            return;
        }

        var databaseDir = Path.Combine(target, "srv/nas/database");
        var databasePath = Path.Combine(databaseDir, "nas.db");
        Directory.CreateDirectory(databaseDir);

        await Task.Run(() => SeedFortosUserDb(databasePath, username, password), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Idempotently creates the FortOS user database at the given SQLite file and
    /// writes the first admin. Pure function (depends only on the file referenced
    /// by dbPath), convenient for unit testing.
    /// </summary>
    internal static void SeedFortosUserDb(string databasePath, string username, string password)
    {
        var connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            DefaultTimeout = 30,
            // One-time database write; pooling is disabled to avoid lingering handles (the install process still has to unmount the target disk afterwards).
            Pooling = false,
        }.ToString();

        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
        connection.Open();

        using (var create = connection.CreateCommand())
        {
            // Keep consistent with the users table schema in FortOS.Core/Data/DatabaseProvider.cs.
            create.CommandText = """
                CREATE TABLE IF NOT EXISTS users (
                    username TEXT PRIMARY KEY,
                    password_hash TEXT NOT NULL,
                    display_name TEXT,
                    email TEXT,
                    totp_secret TEXT,
                    failed_attempts INT DEFAULT 0,
                    locked_until TEXT,
                    created_at TEXT NOT NULL,
                    roles_json TEXT DEFAULT '[]'
                );
                """;
            create.ExecuteNonQuery();
        }

        using (var count = connection.CreateCommand())
        {
            count.CommandText = "SELECT COUNT(*) FROM users WHERE username = $username;";
            count.Parameters.AddWithValue("$username", username);
            var exists = Convert.ToInt64(count.ExecuteScalar() ?? 0L) > 0;
            if (exists)
            {
                // Retry install scenario: skip if the database already contains this user; do not overwrite existing data.
                return;
            }
        }

        using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO users (username, password_hash, display_name, email, failed_attempts, locked_until, created_at, roles_json)
            VALUES ($username, $password_hash, $display_name, $email, 0, NULL, $created_at, $roles_json);
            """;
        insert.Parameters.AddWithValue("$username", username);
        insert.Parameters.AddWithValue("$password_hash", BCrypt.Net.BCrypt.HashPassword(password, 12));
        insert.Parameters.AddWithValue("$display_name", username);
        insert.Parameters.AddWithValue("$email", DBNull.Value);
        insert.Parameters.AddWithValue("$created_at", DateTimeOffset.UtcNow.ToString("O"));
        // The first user automatically gets the admin role (consistent with
        // IdentityService.CreateLocalUserAsync).
        // Serialize with the source-generated context (reflection-based
        // JsonSerializer is disabled under the installer's PublishTrimmed).
        insert.Parameters.AddWithValue(
            "$roles_json",
            System.Text.Json.JsonSerializer.Serialize(
                new[] { "admin", "user" }, Models.InstallerJsonContext.Default.StringArray));
        insert.ExecuteNonQuery();
    }

    private async Task CleanupLiveResidueAsync(string target, CancellationToken ct)
    {
        // Disable live-config services so the target system is not treated as a live session at boot.
        await _chroot.RunScriptAsync(
            target,
            "systemctl disable live-config.service 2>/dev/null || true; systemctl disable live-boot.service 2>/dev/null || true; systemctl disable fortos-installer.service 2>/dev/null || true",
            ct).ConfigureAwait(false);

        // Delete copied SSH host keys; they are regenerated on first boot.
        try
        {
            foreach (var file in Directory.GetFiles(Path.Combine(target, "etc/ssh"), "ssh_host_*", SearchOption.TopDirectoryOnly))
            {
                File.Delete(file);
            }
        }
        catch
        {
            // Skip if the directory does not exist or permissions are insufficient.
        }
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>Normalize a hostname: keep alphanumerics and hyphens (the hostname standard allows [a-zA-Z0-9-]).</summary>
    private static string SanitizeHostname(string hostname)
    {
        var sanitized = string.Concat(hostname.Where(c => char.IsLetterOrDigit(c) || c == '-'));
        return string.IsNullOrEmpty(sanitized) ? "fortos" : sanitized.ToLowerInvariant();
    }

    private static string ShellQuote(string value) => value.Replace("'", "'\\''");

    private static void WriteFile(string target, string relativePath, string content)
        => TargetFileWriter.Write(target, relativePath, content);

    private static string? ReadLiveFile(string target, string relativePath)
        => TargetFileWriter.Read(target, relativePath);
}
