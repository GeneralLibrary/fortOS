using FortOS.Platform.Execution;

namespace FortOS.Platform.Linux;

/// <summary>
/// Linux 挂载状态探测：破坏性磁盘操作（格式化 / 擦除分区表）执行前必须确认
/// 目标设备未被挂载，否则正在使用的文件系统会被静默破坏。挂载检查作为纵深
/// 防御的兜底，不依赖调用方是否已做确认。
/// </summary>
internal static class LinuxMountProbe
{
    /// <summary>
    /// 从 /proc/mounts 检查设备（含其全部分区）是否被挂载。探测本身失败（如
    /// /proc/mounts 不可读）时 fail-closed：拒绝破坏性操作，而不是默认放行。
    /// 分区匹配规则：整盘（/dev/sda）自身、数字分区（/dev/sda1）、NVMe 风格
    /// 分区（/dev/nvme0n1p1）都会被识别。
    /// </summary>
    internal static async Task EnsureNotMountedAsync(CommandExecutor executor, string device, CancellationToken ct)
    {
        string[] lines;
        try
        {
            lines = await File.ReadAllLinesAsync("/proc/mounts", ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Cannot read /proc/mounts to verify {device} is unmounted; refusing the destructive operation.", ex);
        }

        var normalized = device.TrimEnd('/');
        foreach (var line in lines)
        {
            var source = line.Split(' ', 2)[0];
            if (IsDeviceOrPartition(source, normalized))
            {
                throw new InvalidOperationException($"Device {device} (or one of its partitions) is currently mounted; refusing to run a destructive operation on it.");
            }
        }
    }

    /// <summary>判断挂载源是否为目标设备或其分区（/dev/sda、/dev/sda1、/dev/nvme0n1p1）。</summary>
    private static bool IsDeviceOrPartition(string mountedSource, string device)
    {
        if (string.Equals(mountedSource, device, StringComparison.Ordinal))
        {
            return true;
        }

        if (!mountedSource.StartsWith(device, StringComparison.Ordinal) || mountedSource.Length <= device.Length)
        {
            return false;
        }

        // 分区号紧跟在设备名后：数字（sda1）或 "p"+数字（nvme0n1p1）。
        var next = mountedSource[device.Length];
        if (char.IsDigit(next))
        {
            return true;
        }

        return next == 'p' && mountedSource.Length > device.Length + 1 && char.IsDigit(mountedSource[device.Length + 1]);
    }
}
