namespace FortOS.Core;

/// <summary>
/// Unified safe path utilities: all boundary checks requiring "the path must be under an allowed root" should go through here,
/// to avoid each caller writing its own copy and behavior drift causing inconsistent permission boundaries (historically there were 4 copies with different behavior).
/// This class only does string-level normalization (unifying separators, resolving "." / ".."); it does not resolve symlinks —
/// symlink escapes must be handled by the caller by resolving the real path via realpath on Linux before passing it in
/// (see FortOS.Modules.Share.Services.FilePathResolver.ResolveRealPathAsync).
/// </summary>
public static class PathSafety
{
    /// <summary>Default NAS data root; deployments override it via the FortOS_DATA_ROOT environment variable.</summary>
    public const string DefaultDataRoot = "/srv/nas";

    /// <summary>
    /// Resolves the effective data root from a configured value (e.g. the FortOS_DATA_ROOT
    /// environment variable), falling back to <see cref="DefaultDataRoot"/>. Centralizes the
    /// fallback so every caller agrees on the same default.
    /// </summary>
    public static string ResolveDataRoot(string? configured)
        => string.IsNullOrWhiteSpace(configured) ? DefaultDataRoot : configured;

    /// <summary>
    /// Normalizes the path segment by segment, resolving "." and "..", and outputs an absolute path (Unix-style; Windows drive-letter
    /// paths keep the drive letter). Does not rely on the host file system's <see cref="Path.GetFullPath"/> semantics,
    /// so it correctly handles escapes like /srv/nas/../etc even on a Windows dev machine; ".." above the root
    /// is ignored and cannot escape above the root.
    /// </summary>
    public static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var unix = path.Replace('\\', '/');

        // Relative paths are resolved by the host, then converted to absolute form.
        if (!unix.StartsWith("/", StringComparison.Ordinal))
        {
            unix = Path.GetFullPath(path).Replace('\\', '/');
        }

        // Root prefix: "/" for Unix absolute paths; Windows drive-letter paths (C:/...) keep the drive part,
        // otherwise "C:" would be wrongly treated as a plain directory segment during concatenation (C:\x becoming /C:/x).
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
            // A drive segment (e.g. "C:") is not a directory itself, so skip it.
            if (segment.Length == 2 && segment[1] == ':' && char.IsLetter(segment[0]))
            {
                continue;
            }

            switch (segment)
            {
                case ".":
                    continue;
                case "..":
                    // ".." pops the stack upward; ignored when already at the root, so it cannot escape above the root.
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
    /// Determines whether <paramref name="path"/> is <paramref name="root"/> itself or lies beneath it.
    /// Both sides are normalized first, then a prefix comparison with boundary separators is done
    /// to prevent /data/share2 from being misjudged as lying beneath /data/share.
    /// </summary>
    public static bool IsPathUnderRoot(string path, string root)
    {
        var normalizedPath = NormalizePath(path);
        var normalizedRoot = NormalizePath(root).TrimEnd('/');
        if (normalizedRoot.Length == 0)
        {
            // The root must be a concrete directory; the root path "/" itself is not an allowed root (it would allow everything).
            return false;
        }

        return string.Equals(normalizedPath, normalizedRoot, StringComparison.Ordinal)
            || normalizedPath.StartsWith(normalizedRoot + "/", StringComparison.Ordinal);
    }
}
