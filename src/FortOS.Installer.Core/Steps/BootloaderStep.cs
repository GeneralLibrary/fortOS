using FortOS.Installer.Core.Models;
using FortOS.Installer.Core.Session;
using FortOS.Installer.Core.Tools;

namespace FortOS.Installer.Core.Steps;

/// <summary>
/// 引导安装步骤(设计稿 5.5):按检测到的引导方式安装 GRUB 并生成配置。
/// </summary>
public sealed class BootloaderStep : IInstallStep
{
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
    }
}
