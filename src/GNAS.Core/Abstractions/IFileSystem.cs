namespace GNAS.Core;

/// <summary>文件系统抽象。</summary>
public interface IFileSystem
{
    /// <summary>挂载文件系统。</summary>
    Task MountAsync(string device, string mountPoint, string fsType, CancellationToken ct);
    /// <summary>卸载文件系统。</summary>
    Task UnmountAsync(string mountPoint, CancellationToken ct);
    /// <summary>格式化文件系统。</summary>
    Task FormatAsync(string device, string fsType, CancellationToken ct);
    /// <summary>获取文件系统信息。</summary>
    Task<FsInfo> GetFilesystemInfoAsync(string mountPoint, CancellationToken ct);
}
