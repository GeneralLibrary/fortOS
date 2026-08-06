using System.Text.RegularExpressions;
using FortOS.Installer.Core.Models;
using FortOS.Installer.Core.Session;
using FortOS.Installer.Core.Tools;

namespace FortOS.Installer.Core.Steps;

/// <summary>
/// 引导安装步骤(设计稿 5.5):按检测到的引导方式安装 GRUB 并生成配置。
/// </summary>
public sealed class BootloaderStep : IInstallStep
{
    /// <summary>grub.cfg 中引导内核的命令行(linux / linux16 / linuxefi),要求携带内核路径。</summary>
    private static readonly Regex LinuxEntryLine = new(
        @"^\s*linux(?:16|efi)?\s+\S+", RegexOptions.Compiled);

    /// <summary>grub.cfg 中加载 initramfs 的命令行(initrd / initrdefi),要求携带镜像路径。</summary>
    private static readonly Regex InitrdEntryLine = new(
        @"^\s*initrd(?:efi)?\s+\S+", RegexOptions.Compiled);

    private readonly GrubTool _grub;
    private readonly ChrootRunner _chroot;

    public BootloaderStep(GrubTool grub, ChrootRunner chroot)
    {
        _grub = grub;
        _chroot = chroot;
    }

    public string Name => "Bootloader";

    public InstallerPhase Phase => InstallerPhase.Bootloader;

    public async Task ExecuteAsync(InstallContext context, CancellationToken ct)
    {
        var target = context.TargetMount;
        switch (context.BootMode)
        {
            case BootModeKind.Uefi:
                await _grub.InstallUefiAsync(target, $"{target}/boot/efi", ct).ConfigureAwait(false);
                break;
            case BootModeKind.Bios:
                await _grub.InstallBiosAsync(target, context.Config.SystemDisk, ct).ConfigureAwait(false);
                break;
            default:
                throw new Exceptions.StepException(Name, $"Unknown boot mode: {context.BootMode}");
        }

        // 需要绑定挂载(grub-mkconfig 在 chroot 内执行)。ChrootStep 已绑定过,
        // 重复调用是幂等的(重复 mount --bind 失败被忽略);重试场景(失败清理
        // 已卸载)下保证 chroot 环境就绪。
        await _chroot.BindMountsAsync(target, ct).ConfigureAwait(false);
        await _grub.MakeConfigAsync(target, ct).ConfigureAwait(false);

        // grub-mkconfig 在 chroot 内静默探测;若它找不到内核/initrd 或探测
        // root 设备失败,会输出缺失 linux/initrd/root= 的 grub.cfg 而**不报错**,
        // 安装照常"成功"但重启必崩(VFS: Unable to mount root fs on
        // unknown-block(0,0))。生成后必须校验,失败即中止安装并给出可诊断信息。
        var grubCfg = await File.ReadAllTextAsync(
            Path.Combine(target, "boot/grub/grub.cfg"), ct).ConfigureAwait(false);
        ValidateGrubConfig(grubCfg);
    }

    /// <summary>
    /// 校验 grub-mkconfig 产出的 grub.cfg 具备可启动要素:至少一个 menuentry,
    /// linux 行携带 root= 参数,且存在 initrd 行。纯函数,可单测。
    /// </summary>
    internal static void ValidateGrubConfig(string grubConfig)
    {
        if (string.IsNullOrWhiteSpace(grubConfig))
        {
            throw new Exceptions.StepException(
                "Bootloader",
                "grub-mkconfig produced an empty grub.cfg — the installed system has no boot menu.");
        }

        var lines = grubConfig.Split('\n');
        var hasEntry = lines.Any(l => l.TrimStart().StartsWith("menuentry ", StringComparison.Ordinal));
        if (!hasEntry)
        {
            throw new Exceptions.StepException(
                "Bootloader",
                "grub.cfg contains no menuentry — grub-mkconfig failed to enumerate installed kernels.");
        }

        // 只统计实际内核条目(路径含 vmlinuz);memtest86+ 的
        // "linux16 /boot/memtest86+.bin" 等非内核行不参与校验,避免误报。
        var linuxLines = lines
            .Where(l => LinuxEntryLine.IsMatch(l) && l.Contains("/boot/vmlinuz", StringComparison.Ordinal))
            .ToList();
        if (linuxLines.Count == 0)
        {
            throw new Exceptions.StepException(
                "Bootloader",
                "grub.cfg contains no linux kernel lines — /boot has no installed kernel image.");
        }
        // grub-mkconfig 对每个内核条目使用同一 root 探测结果,所有内核行都应携带
        // root=;逐条要求可防止个别条目缺失时被当作可启动配置放行。
        if (linuxLines.Any(l => !l.Contains("root=", StringComparison.Ordinal)))
        {
            throw new Exceptions.StepException(
                "Bootloader",
                "a grub.cfg kernel line lacks the root= parameter — the kernel cannot locate the root filesystem (boot fails with 'VFS: Unable to mount root fs on unknown-block(0,0)').");
        }

        var hasInitrd = lines.Any(l => InitrdEntryLine.IsMatch(l));
        if (!hasInitrd)
        {
            throw new Exceptions.StepException(
                "Bootloader",
                "grub.cfg contains no initrd line — no initramfs was registered for the kernel. Run 'update-initramfs -u' inside the target system (or fix the copy step) and re-run grub-mkconfig.");
        }
    }
}
