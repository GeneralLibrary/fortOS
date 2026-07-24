namespace GNAS.Modules.Share.Services;

/// <summary>共享回收站服务，提供删除迁移与保留清理。</summary>
public sealed class RecycleBinService
{
    /// <summary>将文件移动到按用户隔离的回收站。</summary>
    public string MoveToRecycleBin(string sharePath, string filePath, string username)
    {
        ShareValidation.ValidatePath(sharePath);
        ShareValidation.ValidatePath(filePath);
        ShareValidation.ValidateName(username);
        var root = Path.GetFullPath(sharePath);
        var source = Path.GetFullPath(filePath);
        if (!source.StartsWith(root, StringComparison.Ordinal))
        {
            throw new ArgumentException("文件必须位于共享目录内。", nameof(filePath));
        }

        var relative = Path.GetRelativePath(root, source);
        var target = Path.Combine(root, ".recycle", username, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(source, target, overwrite: true);
        return target;
    }

    /// <summary>删除超过保留天数的回收站文件。</summary>
    public int Cleanup(string sharePath, int retentionDays)
    {
        ShareValidation.ValidatePath(sharePath);
        var recycle = Path.Combine(sharePath, ".recycle");
        if (!Directory.Exists(recycle))
        {
            return 0;
        }

        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);
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
