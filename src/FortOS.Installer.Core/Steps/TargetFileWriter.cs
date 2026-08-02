namespace FortOS.Installer.Core.Steps;

/// <summary>
/// 目标 rootfs 文件读写辅助:chroot 配置阶段与收尾阶段共用,
/// 统一处理 Windows/Unix 路径分隔符与父目录创建。
/// </summary>
public static class TargetFileWriter
{
    /// <summary>在目标 rootfs 内写文件(relativePath 用 Unix 风格,如 "etc/fstab")。</summary>
    public static void Write(string target, string relativePath, string content)
    {
        var fullPath = Path.Combine(target, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    /// <summary>读取目标 rootfs 内文件;不存在或不可读时返回 null。</summary>
    public static string? Read(string target, string relativePath)
    {
        try
        {
            return File.ReadAllText(Path.Combine(target, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch
        {
            return null;
        }
    }
}
