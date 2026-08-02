using FortOS.Installer.Core.Models;
using FortOS.Installer.Core.Session;
using FortOS.Installer.Core.Tools;

namespace FortOS.Installer.Core.Steps;

/// <summary>
/// chroot 目标系统配置步骤(设计稿 5.4):fstab、hostname、时区/locale/键盘、
/// 管理员账户、服务启用、fortos.env、网络、清理 live 残留。
/// </summary>
public sealed class ChrootStep : IInstallStep
{
    /// <summary>与 eng/iso/config/hooks/live/0100-fortos-runtime.hook.chroot 一致的服务启用清单(含可选 nut-monitor)。</summary>
    private static readonly string[] EnabledServices =
    [
        "docker.service", "containerd.service", "smbd.service", "nmbd.service",
        "nfs-server.service", "nfs-mountd.service", "rpcbind.service",
        "vsftpd.service", "ssh.service", "NetworkManager.service", "fortos.service",
        "nut-monitor.service",
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
        await EnableServicesAsync(target, ct).ConfigureAwait(false);
        await ConfigureNetworkAsync(context, target, ct).ConfigureAwait(false);
        await ConfigureRaidAsync(context, target, ct).ConfigureAwait(false);
        await CleanupLiveResidueAsync(target, ct).ConfigureAwait(false);

        context.Summary.Hostname = SanitizeHostname(config.Network.Hostname); // 记录实际写入的值
        context.Summary.Username = config.Account.Username;
        context.Summary.Language = config.Locale.Language;
        context.Summary.Timezone = config.Account.Timezone;
    }

    // ---------------------------------------------------------------------
    // 文件写入(直接操作 /target,可单测)
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

    /// <summary>生成 /etc/fstab 内容(纯函数,可单测)。</summary>
    internal static string BuildFstab(InstallContext context)
    {
        var rootFs = context.Config.RootFs == RootFileSystem.Btrfs ? "btrfs" : "ext4";
        // root UUID 缺失说明 blkid 收集失败,直接报错而非生成坏 fstab。
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
            // LUKS 数据盘必须经 crypttab 解锁后以 mapper 设备挂载。
            // 若容器 UUID 缺失(收集失败),应失败而非回退到直挂——那会在重启后挂载失败。
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

    /// <summary>生成 /etc/crypttab 内容(LUKS 数据盘;纯函数,可单测)。</summary>
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
        // 替换可能存在的旧符号链接。
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
            // 符号链接删除失败不致命。
        }
        try
        {
            File.CreateSymbolicLink(localtime, $"/usr/share/zoneinfo/{tz}");
        }
        catch
        {
            // 目标 zoneinfo 不存在时跳过(时区值本身已写入 /etc/timezone)。
        }
    }

    private static void WriteLocale(InstallContext context, string target)
        => WriteFile(target, "etc/default/locale", $"LANG={context.Config.Locale.Language}\n");

    private static void WriteKeyboard(InstallContext context, string target)
        => WriteFile(target, "etc/default/keyboard", $"XKBLAYOUT=\"{context.Config.Locale.Keyboard}\"\nXKBMODEL=\"pc105\"\n");

    private static void WriteFortosEnv(InstallContext context, string target)
    {
        WriteFile(target, "etc/fortos/fortos.env", "FortOS_DATA_ROOT=/srv/nas\n");
        var version = ReadLiveFile(target, "etc/fortos/version");
        if (!string.IsNullOrEmpty(version))
        {
            context.Summary.FortosVersion = version.Trim();
        }
    }

    // ---------------------------------------------------------------------
    // chroot 内命令
    // ---------------------------------------------------------------------

    private async Task ConfigureUserAsync(InstallContext context, string target, CancellationToken ct)
    {
        var username = context.Config.Account.Username;
        var home = $"/home/{username}";
        // 用户名已由 ValidateConfig 限定为安全子集;这里仍加引号做纵深防御。
        var qUsername = ShellQuote(username);
        var qHome = ShellQuote(home);

        // 幂等创建:用户已存在(重试场景)则跳过,其余失败(磁盘满等)如实报错。
        await _chroot.RunScriptAsync(
            target,
            $"id -u '{qUsername}' >/dev/null 2>&1 || useradd -m -d '{qHome}' -s /bin/bash -G sudo {qUsername}",
            ct).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(context.Config.Account.Password))
        {
            // 密码经 stdin 传给 chpasswd,不进命令行。
            await _chroot.RunScriptAsync(
                target,
                "chpasswd",
                ct,
                standardInput: $"{username}:{context.Config.Account.Password}\n").ConfigureAwait(false);
        }

        // sudoers:FortOS 管理员无密码 sudo。
        WriteFile(target, "etc/sudoers.d/90-fortos-admin", $"{username} ALL=(ALL) NOPASSWD:ALL\n");

        if (!string.IsNullOrWhiteSpace(context.Config.Account.SshPublicKey))
        {
            var sshDir = Path.Combine(target, home.TrimStart('/'), ".ssh");
            Directory.CreateDirectory(sshDir);
            WriteFile(target, $"{home.TrimStart('/')}/.ssh/authorized_keys", context.Config.Account.SshPublicKey.TrimEnd() + "\n");
            // 权限由 chroot 内 chown 校正。
            await _chroot.RunScriptAsync(
                target,
                $"chown -R '{qUsername}':'{qUsername}' '{qHome}/.ssh' && chmod 700 '{qHome}/.ssh' && chmod 600 '{qHome}/.ssh/authorized_keys'",
                ct).ConfigureAwait(false);
        }
    }

    private async Task EnableServicesAsync(string target, CancellationToken ct)
    {
        // 主服务启用失败必须可见(否则安装报成功但目标系统服务全没启用)。
        // nut-monitor 为可选(NAS 无 UPS 时不存在),单独容忍。
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
            // NetworkManager 默认对以太网启用 DHCP,无需写入 connection。
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
        // 记录数组供重启后自动组装(mdadm 失败必须可见),并刷新 initramfs(容忍失败)。
        await _chroot.RunScriptAsync(
            target,
            "mdadm --detail --scan > /etc/mdadm/mdadm.conf || { echo 'mdadm scan failed' >&2; exit 1; }; update-initramfs -u 2>/dev/null || true",
            ct).ConfigureAwait(false);
    }

    private async Task CleanupLiveResidueAsync(string target, CancellationToken ct)
    {
        // 禁用 live-config 服务,避免目标系统启动时仍按 live 会话处理。
        await _chroot.RunScriptAsync(
            target,
            "systemctl disable live-config.service 2>/dev/null || true; systemctl disable live-boot.service 2>/dev/null || true; systemctl disable fortos-installer.service 2>/dev/null || true",
            ct).ConfigureAwait(false);

        // 删除复制的 SSH 主机密钥,首次启动重新生成。
        try
        {
            foreach (var file in Directory.GetFiles(Path.Combine(target, "etc/ssh"), "ssh_host_*", SearchOption.TopDirectoryOnly))
            {
                File.Delete(file);
            }
        }
        catch
        {
            // 目录不存在或权限不足时跳过。
        }
    }

    // ---------------------------------------------------------------------
    // 辅助
    // ---------------------------------------------------------------------

    /// <summary>规范化主机名:保留字母数字与连字符(hostname 标准允许 [a-zA-Z0-9-])。</summary>
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
