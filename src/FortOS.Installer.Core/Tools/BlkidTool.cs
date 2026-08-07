using FortOS.Installer.Core.Exceptions;

namespace FortOS.Installer.Core.Tools;

/// <summary>
/// <c>blkid</c> adapter: reads the file system UUID on block devices (design draft 5.4).
/// Independent of <see cref="LsblkTool"/>: on devices such as loop, lsblk's kernel block attributes
/// may not refresh after mkfs, whereas blkid probes the disk directly and the result is reliable.
/// </summary>
public sealed class BlkidTool : ITool
{
    private readonly IProcessRunner _runner;

    public BlkidTool(IProcessRunner runner) => _runner = runner;

    public string Name => "blkid";

    /// <summary>
    /// Returns the device's file system UUID; returns null when there is no file system (e.g. a BIOS boot partition) or blkid is unavailable.
    /// </summary>
    public async Task<string?> GetUuidAsync(string devicePath, CancellationToken ct)
    {
        var result = await _runner
            .RunAsync("blkid", ["-s", "UUID", "-o", "value", devicePath], ct, throwOnNonZeroExit: false)
            .ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return null;
        }
        var uuid = result.Stdout.Trim();
        return string.IsNullOrEmpty(uuid) ? null : uuid;
    }
}
