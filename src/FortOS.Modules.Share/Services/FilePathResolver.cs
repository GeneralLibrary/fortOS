using FortOS.Core;

namespace FortOS.Modules.Share.Services;

/// <summary>
/// Resolves and validates file paths against the NAS sandbox roots. Extracted from
/// FileManagerService so that upload sessions and the recycle bin share one path-safety
/// implementation instead of each writing its own copy.
/// </summary>
public sealed class FilePathResolver
{
    private readonly IFortOSConfiguration _configuration;
    private readonly ShareModule? _shareModule;
    private readonly IProcessManager? _processManager;

    /// <summary>
    /// Initialize the path resolver.
    /// </summary>
    /// <param name="configuration">Configuration provider (files:allowed_roots).</param>
    /// <param name="shareModule">Optional share module; when present, its share paths are also allowed roots.</param>
    /// <param name="processManager">Optional process runner used to resolve real paths via realpath.</param>
    public FilePathResolver(IFortOSConfiguration configuration, ShareModule? shareModule = null, IProcessManager? processManager = null)
    {
        _configuration = configuration;
        _shareModule = shareModule;
        _processManager = processManager;
    }

    /// <summary>
    /// Resolves a caller-supplied path to its real, validated absolute form. Rejects paths with
    /// newlines, resolves symlinks via realpath, and enforces that the result lies under one of
    /// the allowed roots. The returned path is used for all subsequent IO, avoiding a TOCTOU
    /// window where the symlink is swapped after validation.
    /// </summary>
    public async Task<string> ResolvePathAsync(string path, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.Contains('\n') || path.Contains('\r'))
        {
            throw new ArgumentException("Path cannot contain newlines.", nameof(path));
        }

        // Resolve the real path first (following symlinks): a symlink inside the share directory can point outside it, and a bare string-prefix
        // check would wrongly treat /share/link-to-/etc as being inside the allowed root.
        var resolvedPath = await ResolveRealPathAsync(Path.GetFullPath(path), ct).ConfigureAwait(false);
        var allowedRoots = await GetAllowedRootsAsync(ct).ConfigureAwait(false);
        if (!allowedRoots.Any(root => PathSafety.IsPathUnderRoot(resolvedPath, root)))
        {
            throw new PermissionDeniedException($"Path exceeds allowed directories: {path}");
        }

        // Return the real path: all subsequent IO is based on it, avoiding a TOCTOU window where the symlink is swapped after validation.
        return resolvedPath;
    }

    /// <summary>
    /// Resolves the real form of a path: uses realpath -m to expand symlinks of existing components (-m does not require the path
    /// to exist, covering the scenario of creating new files). When realpath is unavailable (e.g. the tool is missing in a container) or fails, it falls back to the
    /// normalized path, at least keeping protection against "..".
    /// </summary>
    public async Task<string> ResolveRealPathAsync(string path, CancellationToken ct)
    {
        if (_processManager is null)
        {
            return PathSafety.NormalizePath(path);
        }

        try
        {
            var result = await _processManager.ExecuteCommandAsync(new ProcessStartConfig
            {
                ExecutablePath = "realpath",
                Arguments = "-m " + QuoteForShell(path),
                TimeoutSeconds = 5,
            }, ct).ConfigureAwait(false);
            if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Stdout))
            {
                return PathSafety.NormalizePath(result.Stdout.Trim());
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // best-effort: when realpath is unavailable, fall back to the normalized path without blocking file operations.
        }

        return PathSafety.NormalizePath(path);
    }

    /// <summary>
    /// Returns every directory under which file operations are permitted: the data root, any
    /// explicitly configured roots (files:allowed_roots), and every registered share path.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetAllowedRootsAsync(CancellationToken ct)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            PathSafety.NormalizePath(GetDataRoot()),
        };
        foreach (var root in ReadConfiguredRoots())
        {
            roots.Add(PathSafety.NormalizePath(root));
        }

        foreach (var share in await SafeListSharesAsync(ct).ConfigureAwait(false))
        {
            roots.Add(PathSafety.NormalizePath(Path.GetFullPath(share.Path)));
        }

        return roots.ToArray();
    }

    /// <summary>
    /// Returns the share root that contains <paramref name="path"/> (the most specific one when
    /// multiple shares overlap), used to place recycle-bin entries next to the share they came from.
    /// </summary>
    public async Task<string> ResolveShareRootAsync(string path, CancellationToken ct)
    {
        var candidates = new List<string>();
        foreach (var share in await SafeListSharesAsync(ct).ConfigureAwait(false))
        {
            candidates.Add(Path.GetFullPath(share.Path));
        }

        candidates.Add(GetDataRoot());
        var fullPath = Path.GetFullPath(path);
        var normalizedPath = PathSafety.NormalizePath(fullPath);
        var root = candidates
            .Select(Path.GetFullPath)
            .Select(p => new { Original = p, Normalized = PathSafety.NormalizePath(p) })
            .Where(c => PathSafety.IsPathUnderRoot(normalizedPath, c.Normalized))
            .OrderByDescending(c => c.Normalized.Length)
            .FirstOrDefault();
        if (root is null)
        {
            throw new PermissionDeniedException($"Path is not under a shared directory or the data root directory: {path}");
        }

        return root.Original;
    }

    /// <summary>Returns the effective NAS data root (FortOS_DATA_ROOT or the default).</summary>
    public static string GetDataRoot() => PathSafety.ResolveDataRoot(Environment.GetEnvironmentVariable("FortOS_DATA_ROOT"));

    /// <summary>Process runner, shared with services that need to execute shell commands on resolved paths.</summary>
    public IProcessManager ProcessManager => _processManager ?? throw new InvalidOperationException("IProcessManager is not registered.");

    private async Task<IReadOnlyList<ShareDefinition>> SafeListSharesAsync(CancellationToken ct)
    {
        if (_shareModule is null)
        {
            return [];
        }

        try
        {
            return await _shareModule.ListSharesAsync(ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Share module has not been initialized yet (module host not started / degraded mode);
            // there are no shares to include, file operations must keep working regardless.
            return [];
        }
    }

    private string[] ReadConfiguredRoots()
    {
        var values = _configuration.GetArray("files:allowed_roots") ?? [];
        if (values.Length > 0)
        {
            return values;
        }

        var scalar = _configuration.GetValue("files:allowed_roots");
        return string.IsNullOrWhiteSpace(scalar)
            ? []
            : scalar.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string QuoteForShell(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
