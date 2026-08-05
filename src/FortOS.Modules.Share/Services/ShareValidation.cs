using FortOS.Core;

namespace FortOS.Modules.Share.Services;

/// <summary>Share configuration security validation.</summary>
public static class ShareValidation
{
    private static readonly HashSet<string> SupportedProtocols = new(
        ["smb", "nfs", "ftp"],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>Validate share definition.</summary>
    public static void ValidateShare(ShareDefinition share)
    {
        ArgumentNullException.ThrowIfNull(share);
        ValidateName(share.Name);
        ValidatePath(share.Path);
        if (share.Description?.IndexOfAny(['\r', '\n']) >= 0)
        {
            throw new ArgumentException("Share description cannot contain newlines.", nameof(share));
        }

        if (share.Protocols.Length == 0
            || share.Protocols.Any(protocol => !SupportedProtocols.Contains(protocol)))
        {
            throw new ArgumentException(
                "Share protocols only support smb, nfs, and ftp; WebDAV will not be opened until complete authentication is available.",
                nameof(share));
        }
    }

    /// <summary>Validate share name.</summary>
    public static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name is "." or ".." || name.Any(c => !(char.IsLetterOrDigit(c) || c is '_' or '-' or '.')) || name.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Share name can only contain letters, digits, dots, underscores, and hyphens, and must not allow path traversal.", nameof(name));
        }
    }

    /// <summary>Validate share path.</summary>
    public static void ValidatePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var isAbsolute = Path.IsPathFullyQualified(path) || path.StartsWith("/", StringComparison.Ordinal);
        if (!isAbsolute || path.Contains('\n') || path.Contains('\r'))
        {
            throw new ArgumentException("Share path must be an absolute path without newlines.", nameof(path));
        }

        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Any(s => s == ".."))
        {
            throw new ArgumentException("Share path must not contain path traversal.", nameof(path));
        }

        // 共享路径必须位于数据根之下：否则 /、/etc、/home 等系统目录会被直接
        // 暴露为 SMB/NFS/FTP 共享，整机文件可读。数据根与 FortOS_DATA_ROOT
        // 环境变量一致（缺省 /srv/nas）。
        var root = ResolveDataRoot();
        var normalizedPath = NormalizeForComparison(path);
        var normalizedRoot = NormalizeForComparison(root);
        if (!string.Equals(normalizedPath, normalizedRoot, StringComparison.Ordinal)
            && !normalizedPath.StartsWith(normalizedRoot + "/", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Share path must be located under the data root ({root}).", nameof(path));
        }
    }

    /// <summary>解析数据根：优先环境变量 <c>FortOS_DATA_ROOT</c>，缺省 <c>/srv/nas</c>。</summary>
    public static string ResolveDataRoot()
    {
        var value = Environment.GetEnvironmentVariable("FortOS_DATA_ROOT");
        return string.IsNullOrWhiteSpace(value) ? "/srv/nas" : value.TrimEnd('/', '\\');
    }

    private static string NormalizeForComparison(string value)
        => value.Replace('\\', '/').TrimEnd('/');
}
