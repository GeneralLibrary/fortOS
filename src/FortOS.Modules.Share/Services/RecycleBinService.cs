using FortOS.Core;

namespace FortOS.Modules.Share.Services;

/// <summary>Share recycle bin service, provides deletion migration and retention cleanup.</summary>
public sealed class RecycleBinService
{
    /// <summary>Moves a file to a user-isolated recycle bin.</summary>
    public string MoveToRecycleBin(string sharePath, string filePath, string username)
    {
        ShareValidation.ValidatePath(sharePath);
        ShareValidation.ValidatePath(filePath);
        ShareValidation.ValidateName(username);
        var root = Path.GetFullPath(sharePath);
        var source = Path.GetFullPath(filePath);
        // 带分隔符边界的根校验：/share 不允许 /share2 的文件进入其回收站。
        if (!PathSafety.IsPathUnderRoot(source, root))
        {
            throw new ArgumentException("File must be located within the share directory.", nameof(filePath));
        }

        var relative = Path.GetRelativePath(root, source);
        var target = Path.Combine(root, ".recycle", username, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(source, target, overwrite: true);
        return target;
    }

    /// <summary>Deletes recycle bin files exceeding the retention period.</summary>
    public int Cleanup(string sharePath, int retentionDays)
    {
        ShareValidation.ValidatePath(sharePath);
        var recycle = Path.Combine(sharePath, ".recycle");
        if (!Directory.Exists(recycle))
        {
            return 0;
        }

        // retentionDays 负值会把 cutoff 推到未来，导致整目录被清空：限制在 [0, 3650]。
        var days = Math.Clamp(retentionDays, 0, 3650);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-days);
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(recycle, "*", SearchOption.AllDirectories))
        {
            if (File.GetLastWriteTimeUtc(file) < cutoff.UtcDateTime)
            {
                File.Delete(file);
                count++;
            }
        }

        return count;
    }
}
