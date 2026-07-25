using GNAS.Core;

namespace GNAS.Modules.Share.Services;

/// <summary>共享配置安全校验。</summary>
public static class ShareValidation
{
    private static readonly HashSet<string> SupportedProtocols = new(
        ["smb", "nfs", "ftp"],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>校验共享定义。</summary>
    public static void ValidateShare(ShareDefinition share)
    {
        ArgumentNullException.ThrowIfNull(share);
        ValidateName(share.Name);
        ValidatePath(share.Path);
        if (share.Description?.IndexOfAny(['\r', '\n']) >= 0)
        {
            throw new ArgumentException("共享描述不能包含换行。", nameof(share));
        }

        if (share.Protocols.Length == 0
            || share.Protocols.Any(protocol => !SupportedProtocols.Contains(protocol)))
        {
            throw new ArgumentException(
                "共享协议仅支持 smb、nfs 和 ftp；WebDAV 在具备完整认证前不会开放。",
                nameof(share));
        }
    }

    /// <summary>校验共享名称。</summary>
    public static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name is "." or ".." || name.Any(c => !(char.IsLetterOrDigit(c) || c is '_' or '-' or '.')) || name.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("共享名称只能包含字母、数字、点、下划线和短横线，且不能为路径遍历。", nameof(name));
        }
    }

    /// <summary>校验共享路径。</summary>
    public static void ValidatePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var isAbsolute = Path.IsPathFullyQualified(path) || path.StartsWith("/", StringComparison.Ordinal);
        if (!isAbsolute || path.Contains('\n') || path.Contains('\r'))
        {
            throw new ArgumentException("共享路径必须为不含换行的绝对路径。", nameof(path));
        }

        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Any(s => s == ".."))
        {
            throw new ArgumentException("共享路径不能包含路径遍历。", nameof(path));
        }
    }
}
