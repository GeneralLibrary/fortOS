namespace FortOS.Core;

/// <summary>
/// 统一的安全路径工具：所有「路径必须位于允许根之下」的边界校验都应经由这里，
/// 避免各处各写一份、行为漂移导致权限边界不一致（历史上有 4 份行为不同的副本）。
/// 本类只做字符串级归一化（统一分隔符、解析 "." / ".."），不解析符号链接——
/// 符号链接逃逸需由调用方在 Linux 上先用 realpath 解析真实路径后再传入
/// （见 FortOS.Modules.Share.Services.FileManagerService.ResolveRealPathAsync）。
/// </summary>
public static class PathSafety
{
    /// <summary>
    /// 按段归一化路径并解析 "." 与 ".."，输出绝对路径（Unix 风格，Windows 盘符
    /// 路径保留盘符）。不依赖宿主文件系统的 <see cref="Path.GetFullPath"/> 语义，
    /// 因此在 Windows 开发机上也能正确判定 /srv/nas/../etc 这类穿越；根之上的
    /// ".." 被忽略，不会逃逸到根之上。
    /// </summary>
    public static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var unix = path.Replace('\\', '/');

        // 相对路径交由宿主解析后再转为绝对形式。
        if (!unix.StartsWith("/", StringComparison.Ordinal))
        {
            unix = Path.GetFullPath(path).Replace('\\', '/');
        }

        // 根前缀：Unix 绝对路径为 "/"；Windows 盘符路径（C:/...）保留盘符部分，
        // 否则段拼接时会错误地把 "C:" 当作普通目录段（C:\x 变成 /C:/x）。
        string rootPrefix;
        if (unix.Length >= 3 && char.IsLetter(unix[0]) && unix[1] == ':' && unix[2] == '/')
        {
            rootPrefix = unix[..3];
        }
        else
        {
            rootPrefix = "/";
        }

        var segments = new List<string>();
        foreach (var segment in unix.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            // 盘符段（如 "C:"）本身不是目录，直接跳过。
            if (segment.Length == 2 && segment[1] == ':' && char.IsLetter(segment[0]))
            {
                continue;
            }

            switch (segment)
            {
                case ".":
                    continue;
                case "..":
                    // ".." 向上弹栈；已在根时忽略，不会逃逸到根之上。
                    if (segments.Count > 0) segments.RemoveAt(segments.Count - 1);
                    continue;
                default:
                    segments.Add(segment);
                    break;
            }
        }

        return rootPrefix + string.Join('/', segments);
    }

    /// <summary>
    /// 判断 <paramref name="path"/> 是 <paramref name="root"/> 本身或位于其之下。
    /// 两端先归一化，再做带边界分隔符的前缀比较，防止 /data/share2 被误判为
    /// 位于 /data/share 之下。
    /// </summary>
    public static bool IsPathUnderRoot(string path, string root)
    {
        var normalizedPath = NormalizePath(path);
        var normalizedRoot = NormalizePath(root).TrimEnd('/');
        if (normalizedRoot.Length == 0)
        {
            // 根必须是具体目录；根路径 "/" 本身不构成允许根（会放行一切）。
            return false;
        }

        return string.Equals(normalizedPath, normalizedRoot, StringComparison.Ordinal)
            || normalizedPath.StartsWith(normalizedRoot + "/", StringComparison.Ordinal);
    }
}
