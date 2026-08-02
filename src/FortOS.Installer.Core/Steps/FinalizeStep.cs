using System.Text.Json;
using FortOS.Installer.Core.Models;
using FortOS.Installer.Core.Session;
using FortOS.Installer.Core.Tools;

namespace FortOS.Installer.Core.Steps;

/// <summary>
/// 收尾步骤:写安装摘要与日志到目标系统、卸载挂载、关闭 LUKS/RAID 设备。
/// 成功与失败路径共用 <see cref="UnmountAsync"/>,保证「可重跑」语义(设计稿 5.1)。
/// </summary>
public sealed class FinalizeStep : IInstallStep
{
    private readonly ChrootRunner _chroot;
    private readonly IProcessRunner _runner;
    private readonly CryptsetupTool _cryptsetup;
    private readonly MdadmTool _mdadm;
    private readonly Func<IReadOnlyList<InstallLogEntry>> _logProvider;

    public FinalizeStep(
        ChrootRunner chroot,
        IProcessRunner runner,
        CryptsetupTool cryptsetup,
        MdadmTool mdadm,
        Func<IReadOnlyList<InstallLogEntry>> logProvider)
    {
        _chroot = chroot;
        _runner = runner;
        _cryptsetup = cryptsetup;
        _mdadm = mdadm;
        _logProvider = logProvider;
    }

    public string Name => "Finalize";

    public InstallerPhase Phase => InstallerPhase.Finalize;

    public async Task ExecuteAsync(InstallContext context, CancellationToken ct)
    {
        var target = context.TargetMount;

        context.Summary.FinishedAt = DateTimeOffset.UtcNow;
        context.Summary.Success = true;

        // 目标分区仍挂载:先写摘要与日志到目标 rootfs。
        TargetFileWriter.Write(target, "etc/fortos/install-summary.json", JsonSerializer.Serialize(context.Summary, InstallerJsonContext.Default.InstallSummary));
        var logText = string.Join('\n', _logProvider().Select(l => $"{l.Timestamp:yyyy-MM-dd HH:mm:ss} [{l.Level}] {l.Message}"));
        TargetFileWriter.Write(target, "var/log/fortos-install.log", logText + "\n");

        await UnmountAsync(_chroot, _runner, _cryptsetup, _mdadm, context, ct).ConfigureAwait(false);
        // sync 失败不应当把已完成安装标记为失败(数据已落盘,重启自愈)。
        await _runner.RunAsync("sync", [], ct, throwOnNonZeroExit: false).ConfigureAwait(false);
    }

    /// <summary>
    /// 卸载/关闭安装期持有的设备,成功与失败路径共用:
    /// 卸载绑定挂载与目标分区 → 关闭 LUKS 映射 / 停止 RAID 数组。
    /// 全程容错(umount/cryptsetup close/mdadm --stop 的失败仅告警),
    /// 保证「可重跑」(设计稿 5.1)。
    /// </summary>
    internal static async Task UnmountAsync(
        ChrootRunner chroot,
        IProcessRunner runner,
        CryptsetupTool cryptsetup,
        MdadmTool mdadm,
        InstallContext context,
        CancellationToken ct)
    {
        var target = context.TargetMount;
        await chroot.UnmountAllAsync(target, ct).ConfigureAwait(false);
        await runner.RunAsync("umount", [$"{target}/boot/efi"], ct, throwOnNonZeroExit: false).ConfigureAwait(false);
        await runner.RunAsync("umount", [target], ct, throwOnNonZeroExit: false).ConfigureAwait(false);

        // 数据盘设备:RAID 数组停止 / LUKS 映射关闭,否则重试时设备已存在。
        if (context.Config.Data.Mode == DataDiskMode.Raid && context.DataDevice is not null)
        {
            await mdadm.StopAsync(context.DataDevice, ct).ConfigureAwait(false);
        }
        else if (context.Config.Data.Mode == DataDiskMode.Luks)
        {
            await cryptsetup.LuksCloseAsync(context.Config.Data.LuksMapperName, ct).ConfigureAwait(false);
        }
    }
}
