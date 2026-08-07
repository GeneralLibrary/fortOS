using FortOS.Core;

namespace FortOS.Modules.Share.Services;

/// <summary>
/// Share recycle bin service: moves deleted paths into a user-isolated .recycle directory
/// beneath their share/data root and restores them back. Also provides retention cleanup.
/// </summary>
public sealed class RecycleBinService
{
    private readonly FilePathResolver _resolver;

    /// <summary>Initializes the recycle bin service.</summary>
    /// <param name="resolver">Path validation and share-root resolution.</param>
    public RecycleBinService(FilePathResolver resolver)
    {
        _resolver = resolver;
    }

    /// <summary>
    /// Moves <paramref name="path"/> into the .recycle directory of the share (or data root)
    /// it lives under, isolating entries per requesting user. The move is a rename within the
    /// same file system, so it is effectively atomic.
    /// </summary>
    public async Task<string> MoveToRecycleAsync(string path, string requestedBy, CancellationToken ct)
    {
        var root = await _resolver.ResolveShareRootAsync(path, ct).ConfigureAwait(false);
        var user = SanitizeUser(requestedBy);
        var relativePath = Path.GetRelativePath(root, path);
        var target = Path.Combine(root, ".recycle", user, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (File.Exists(target) || Directory.Exists(target))
        {
            // A previous soft-delete of the same relative path must not be destroyed: version the
            // new entry with a timestamp suffix so both copies survive and File.Move stays safe
            // regardless of whether the old entry is a file or a directory.
            target = Path.Combine(Path.GetDirectoryName(target)!, $"{Path.GetFileName(target)}.{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.deleted");
        }

        if (File.Exists(path))
        {
            File.Move(path, target);
            return target;
        }

        Directory.Move(path, target);
        return target;
    }

    /// <summary>
    /// Moves a path back from the .recycle directory to <paramref name="destination"/>.
    /// Refuses sources outside .recycle so a caller cannot use restore as a generic move primitive.
    /// </summary>
    public async Task<string> MoveBackAsync(string recyclePath, string destination, CancellationToken ct)
    {
        var source = await _resolver.ResolvePathAsync(recyclePath, ct).ConfigureAwait(false);
        var target = await _resolver.ResolvePathAsync(destination, ct).ConfigureAwait(false);
        if (!source.Contains($"{Path.DirectorySeparatorChar}.recycle{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            && !source.Contains("/.recycle/", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The restore source path must be located under the .recycle directory.", nameof(recyclePath));
        }

        if (!File.Exists(source) && !Directory.Exists(source))
        {
            throw new FileNotFoundException("Path does not exist.", source);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (File.Exists(source))
        {
            File.Move(source, target, overwrite: true);
        }
        else
        {
            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }

            Directory.Move(source, target);
        }

        return target;
    }

    /// <summary>Moves a file to a user-isolated recycle bin.</summary>
    public string MoveToRecycleBin(string sharePath, string filePath, string username)
    {
        ShareValidation.ValidatePath(sharePath);
        ShareValidation.ValidatePath(filePath);
        ShareValidation.ValidateName(username);
        var root = Path.GetFullPath(sharePath);
        var source = Path.GetFullPath(filePath);
        // Root check with a delimiter boundary: /share must not let files from /share2 into its recycle bin.
        if (!PathSafety.IsPathUnderRoot(source, root))
        {
            throw new ArgumentException("File must be located within the share directory.", nameof(filePath));
        }

        var relative = Path.GetRelativePath(root, source);
        var target = Path.Combine(root, ".recycle", username, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (File.Exists(target) || Directory.Exists(target))
        {
            // Never overwrite an earlier recycle entry of the same name.
            target = Path.Combine(Path.GetDirectoryName(target)!, $"{Path.GetFileName(target)}.{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.deleted");
        }

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

        // A negative retentionDays pushes the cutoff into the future and empties the whole directory: clamp it to [0, 3650].
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

    /// <summary>
    /// Normalizes a free-form subject into a safe single directory segment: path separators and
    /// traversal segments are replaced, so ".." can never make the recycle entry escape its root.
    /// </summary>
    private static string SanitizeUser(string requestedBy)
    {
        var value = string.IsNullOrWhiteSpace(requestedBy) ? "unknown" : requestedBy;
        value = value.Replace('\\', '_').Replace('/', '_').Replace(':', '_');
        // Reject path traversal segments: the username must be a single directory segment, since ".." would let the recycle bin directory escape the share root.
        if (value.Contains("..", StringComparison.Ordinal))
        {
            return "unknown";
        }

        return value;
    }
}
