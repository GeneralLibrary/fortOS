namespace FortOS.Core;

/// <summary>File system abstraction.</summary>
public interface IFileSystem
{
    /// <summary>Mount a file system.</summary>
    Task MountAsync(string device, string mountPoint, string fsType, CancellationToken ct);
    /// <summary>Unmount a file system.</summary>
    Task UnmountAsync(string mountPoint, CancellationToken ct);
    /// <summary>Format a file system.</summary>
    Task FormatAsync(string device, string fsType, CancellationToken ct);
    /// <summary>Get file system information.</summary>
    Task<FsInfo> GetFilesystemInfoAsync(string mountPoint, CancellationToken ct);
}
