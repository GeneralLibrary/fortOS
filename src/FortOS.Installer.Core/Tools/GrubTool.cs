namespace FortOS.Installer.Core.Tools;

/// <summary>
/// <c>grub-install</c> / <c>grub-mkconfig</c> 适配器:引导安装(设计稿 5.5)。
/// </summary>
public sealed class GrubTool : ITool
{
    private static readonly TimeSpan GrubTimeout = TimeSpan.FromMinutes(5);
    private readonly IProcessRunner _runner;
    private readonly ChrootRunner _chroot;

    public GrubTool(IProcessRunner runner, ChrootRunner chroot)
    {
        _runner = runner;
        _chroot = chroot;
    }

    public string Name => "grub";

    /// <summary>
    /// 安装 UEFI 引导。使用 <c>--efi-directory</c> / <c>--boot-directory</c>
    /// 直接写入目标分区(需先挂载 EFI 分区到 <paramref name="efiMountPath"/>),
    /// 不依赖 chroot 内环境;Secure Boot 链随 shim-signed 生效。
    /// </summary>
    public async Task InstallUefiAsync(string targetRoot, string efiMountPath, CancellationToken ct)
    {
        await _runner.RunAsync(
            "grub-install",
            [
                "--target=x86_64-efi",
                "--efi-directory=" + efiMountPath,
                "--boot-directory=" + targetRoot + "/boot",
                "--bootloader-id=FortOS",
                "--no-nvram", // live 环境下不写 NVRAM,避免污染宿主;目标系统首次启动由 shim 链引导
                "--modules=part_gpt",
            ],
            ct,
            timeout: GrubTimeout).ConfigureAwait(false);
    }

    /// <summary>安装 Legacy BIOS 引导到磁盘 MBR。</summary>
    public async Task InstallBiosAsync(string targetRoot, string disk, CancellationToken ct)
    {
        await _runner.RunAsync(
            "grub-install",
            ["--target=i386-pc", "--boot-directory=" + targetRoot + "/boot", disk],
            ct,
            timeout: GrubTimeout).ConfigureAwait(false);
    }

    /// <summary>在 chroot 内生成 grub.cfg(需要 /etc/grub.d 脚本环境)。</summary>
    public async Task MakeConfigAsync(string targetRoot, CancellationToken ct)
    {
        await _chroot.RunScriptAsync(
            targetRoot,
            "grub-mkconfig -o /boot/grub/grub.cfg",
            ct,
            timeout: GrubTimeout).ConfigureAwait(false);
    }
}
