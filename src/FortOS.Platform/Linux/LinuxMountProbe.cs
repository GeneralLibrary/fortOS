using FortOS.Platform.Execution;

namespace FortOS.Platform.Linux;

/// <summary>
/// Linux mount-state probe: before destructive disk operations (formatting / wiping the partition table) it must
/// confirm the target device is not mounted, otherwise an in-use file system would be silently destroyed. The mount
/// check is the backstop of defense in depth and does not depend on whether the caller already confirmed it.
/// </summary>
internal static class LinuxMountProbe
{
    /// <summary>
    /// Checks /proc/mounts whether the device (including all of its partitions) is mounted. When the probe itself
    /// fails (e.g., /proc/mounts unreadable) it fail-closes: refuse the destructive operation instead of allowing it
    /// by default. Partition matching: the whole disk itself (/dev/sda), numbered partitions (/dev/sda1), and
    /// NVMe-style partitions (/dev/nvme0n1p1) are all recognized.
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

    /// <summary>Determines whether a mount source is the target device or one of its partitions (/dev/sda, /dev/sda1, /dev/nvme0n1p1).</summary>
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

        // The partition number directly follows the device name: a digit (sda1) or "p" + digit (nvme0n1p1).
        var next = mountedSource[device.Length];
        if (char.IsDigit(next))
        {
            return true;
        }

        return next == 'p' && mountedSource.Length > device.Length + 1 && char.IsDigit(mountedSource[device.Length + 1]);
    }
}
