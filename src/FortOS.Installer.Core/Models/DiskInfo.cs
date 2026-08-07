namespace FortOS.Installer.Core.Models;

/// <summary>A single physical disk (from the structured output of <c>lsblk --json</c>).</summary>
public sealed record DiskInfo
{
    /// <summary>Kernel device name, e.g. <c>sda</c>, <c>nvme0n1</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Device path, e.g. <c>/dev/sda</c>.</summary>
    public required string Path { get; init; }

    /// <summary>Disk size (bytes).</summary>
    public ulong SizeBytes { get; init; }

    /// <summary>Vendor model (may be null).</summary>
    public string? Model { get; init; }

    /// <summary>Serial number (may be null).</summary>
    public string? Serial { get; init; }

    /// <summary>Transport type: sata / nvme / usb / virtio, etc. (may be null).</summary>
    public string? Transport { get; init; }

    /// <summary>Whether this is removable media.</summary>
    public bool IsRemovable { get; init; }

    /// <summary>Whether this is a mechanical (rotational) disk.</summary>
    public bool IsRotational { get; init; }

    /// <summary>Whether the disk is read-only (e.g. a mounted ISO device).</summary>
    public bool IsReadOnly { get; init; }

    /// <summary>Human-readable size, e.g. <c>238.5G</c>.</summary>
    public string SizeHuman => SizeBytes switch
    {
        0 => "0 B",
        < 1_000_000_000 => $"{SizeBytes / 1_000_000d:F1} MB",
        < 1_000_000_000_000 => $"{SizeBytes / 1_000_000_000d:F1} GB",
        _ => $"{SizeBytes / 1_000_000_000_000d:F1} TB",
    };
}
