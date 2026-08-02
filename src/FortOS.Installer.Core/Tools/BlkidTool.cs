using FortOS.Installer.Core.Exceptions;

namespace FortOS.Installer.Core.Tools;

/// <summary>
/// <c>blkid</c> 适配器:读取块设备上的文件系统 UUID(设计稿 5.4)。
/// 独立于 <see cref="LsblkTool"/>:loop 等设备上 lsblk 的内核块属性在 mkfs 后
/// 可能不刷新,而 blkid 直接探测磁盘,结果可靠。
/// </summary>
public sealed class BlkidTool : ITool
{
    private readonly IProcessRunner _runner;

    public BlkidTool(IProcessRunner runner) => _runner = runner;

    public string Name => "blkid";

    /// <summary>
    /// 返回设备的文件系统 UUID;无文件系统(如 BIOS boot 分区)或 blkid 不可用时返回 null。
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
