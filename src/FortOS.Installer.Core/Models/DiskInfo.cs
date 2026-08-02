namespace FortOS.Installer.Core.Models;

/// <summary>一块物理磁盘(来自 <c>lsblk --json</c> 的结构化输出)。</summary>
public sealed record DiskInfo
{
    /// <summary>内核设备名,如 <c>sda</c>、<c>nvme0n1</c>。</summary>
    public required string Name { get; init; }

    /// <summary>设备路径,如 <c>/dev/sda</c>。</summary>
    public required string Path { get; init; }

    /// <summary>磁盘大小(字节)。</summary>
    public ulong SizeBytes { get; init; }

    /// <summary>厂商型号(可能为 null)。</summary>
    public string? Model { get; init; }

    /// <summary>序列号(可能为 null)。</summary>
    public string? Serial { get; init; }

    /// <summary>传输类型:sata / nvme / usb / virtio 等(可能为 null)。</summary>
    public string? Transport { get; init; }

    /// <summary>是否为可移动介质。</summary>
    public bool IsRemovable { get; init; }

    /// <summary>是否为机械盘(旋转介质)。</summary>
    public bool IsRotational { get; init; }

    /// <summary>磁盘是否为只读(如挂载中的 ISO 设备)。</summary>
    public bool IsReadOnly { get; init; }

    /// <summary>人类可读大小,如 <c>238.5G</c>。</summary>
    public string SizeHuman => SizeBytes switch
    {
        0 => "0 B",
        < 1_000_000_000 => $"{SizeBytes / 1_000_000d:F1} MB",
        < 1_000_000_000_000 => $"{SizeBytes / 1_000_000_000d:F1} GB",
        _ => $"{SizeBytes / 1_000_000_000_000d:F1} TB",
    };
}
