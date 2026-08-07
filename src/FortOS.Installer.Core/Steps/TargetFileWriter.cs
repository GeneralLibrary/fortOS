namespace FortOS.Installer.Core.Steps;

/// <summary>
/// Target rootfs file read/write helper: shared by the chroot configuration phase
/// and the finalize phase; uniformly handles Windows/Unix path separators and
/// parent directory creation.
/// </summary>
public static class TargetFileWriter
{
    /// <summary>Write a file inside the target rootfs (relativePath uses Unix style, e.g. "etc/fstab").</summary>
    public static void Write(string target, string relativePath, string content)
    {
        var fullPath = Path.Combine(target, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    /// <summary>Read a file inside the target rootfs; returns null if it does not exist or is unreadable.</summary>
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
