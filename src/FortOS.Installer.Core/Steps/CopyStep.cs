using FortOS.Installer.Core.Session;
using FortOS.Installer.Core.Tools;

namespace FortOS.Installer.Core.Steps;

/// <summary>
/// System copy step: rsync the live rootfs (or squashfs) to the target system (design doc 5.3).
/// </summary>
public sealed class CopyStep : IInstallStep
{
    private readonly RsyncTool _rsync;

    public CopyStep(RsyncTool rsync) => _rsync = rsync;

    public string Name => "Copy";

    public InstallerPhase Phase => InstallerPhase.Copying;

    public async Task ExecuteAsync(InstallContext context, CancellationToken ct)
    {
        await _rsync.CopyAsync(context.SourcePath, context.TargetMount, ct).ConfigureAwait(false);
    }
}
