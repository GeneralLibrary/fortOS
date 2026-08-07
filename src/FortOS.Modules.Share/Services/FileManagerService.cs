using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using FortOS.Core;

namespace FortOS.Modules.Share.Services;

/// <summary>
/// File management service restricted to the NAS sandbox root directory.
/// Path resolution, upload sessions, and the recycle bin live in their own services
/// (FilePathResolver, UploadSessionService, RecycleBinService); this class owns only
/// the CRUD operations, metadata, and Unix permission helpers.
/// </summary>
public sealed partial class FileManagerService
{
    private readonly FilePathResolver _resolver;
    private readonly RecycleBinService _recycleBin;
    private readonly IFortOSConfiguration _configuration;

    /// <summary>
    /// Initialize the file management service.
    /// </summary>
    /// <param name="resolver">Path resolution and sandbox-root validation.</param>
    /// <param name="recycleBin">Recycle bin moves for soft deletes and restores.</param>
    /// <param name="configuration">Configuration provider (files:legacy_max_bytes).</param>
    public FileManagerService(FilePathResolver resolver, RecycleBinService recycleBin, IFortOSConfiguration configuration)
    {
        _resolver = resolver;
        _recycleBin = recycleBin;
        _configuration = configuration;
    }

    /// <summary>List directory contents.</summary>
    public async Task<IReadOnlyList<ManagedFileEntry>> ListAsync(string path, bool recursive, CancellationToken ct)
    {
        var fullPath = await _resolver.ResolvePathAsync(path, ct).ConfigureAwait(false);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Directory does not exist: {fullPath}");
        }

        var entries = new List<ManagedFileEntry>();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = false,
            ReturnSpecialDirectories = false,
            AttributesToSkip = 0,
        };
        foreach (var directory in Directory.EnumerateDirectories(fullPath, "*", options))
        {
            ct.ThrowIfCancellationRequested();
            var info = new DirectoryInfo(directory);
            entries.Add(new ManagedFileEntry
            {
                Path = info.FullName,
                Name = info.Name,
                IsDirectory = true,
                SizeBytes = null,
                ModifiedAt = info.LastWriteTimeUtc,
            });
        }

        foreach (var file in Directory.EnumerateFiles(fullPath, "*", options))
        {
            ct.ThrowIfCancellationRequested();
            var info = new FileInfo(file);
            entries.Add(new ManagedFileEntry
            {
                Path = info.FullName,
                Name = info.Name,
                IsDirectory = false,
                SizeBytes = info.Length,
                ModifiedAt = info.LastWriteTimeUtc,
            });
        }

        return entries.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>Read file content.</summary>
    public async Task<ManagedFileContent> ReadAsync(string path, bool asBase64, CancellationToken ct)
    {
        var fullPath = await _resolver.ResolvePathAsync(path, ct).ConfigureAwait(false);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("File does not exist.", fullPath);
        }

        var max = ReadMaximumLegacyBytes();
        var info = new FileInfo(fullPath);
        if (info.Length > max) throw new IOException($"The legacy JSON content interface supports a maximum of {max} bytes; please use streaming download.");
        var bytes = await File.ReadAllBytesAsync(fullPath, ct).ConfigureAwait(false);
        return asBase64
            ? new ManagedFileContent { Path = fullPath, Encoding = "base64", Content = Convert.ToBase64String(bytes), SizeBytes = bytes.LongLength }
            : new ManagedFileContent { Path = fullPath, Encoding = "text", Content = Encoding.UTF8.GetString(bytes), SizeBytes = bytes.LongLength };
    }

    /// <summary>Write file.</summary>
    public async Task<ManagedFileStat> WriteAsync(string path, string content, string encoding, bool overwrite, CancellationToken ct)
    {
        var fullPath = await _resolver.ResolvePathAsync(path, ct).ConfigureAwait(false);
        if (File.Exists(fullPath) && !overwrite)
        {
            throw new IOException($"File already exists: {fullPath}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        if (Encoding.UTF8.GetByteCount(content) > ReadMaximumLegacyBytes()) throw new IOException("The legacy JSON/base64 write interface content exceeds the limit; please use resumable upload.");
        var bytes = DecodeContent(content, encoding);
        await File.WriteAllBytesAsync(fullPath, bytes, ct).ConfigureAwait(false);
        return await StatAsync(fullPath, ct).ConfigureAwait(false);
    }

    /// <summary>Create directory.</summary>
    public async Task<ManagedFileStat> CreateDirectoryAsync(string path, CancellationToken ct)
    {
        var fullPath = await _resolver.ResolvePathAsync(path, ct).ConfigureAwait(false);
        var info = Directory.CreateDirectory(fullPath);
        return new ManagedFileStat
        {
            Path = info.FullName,
            Exists = true,
            IsDirectory = true,
            SizeBytes = null,
            ModifiedAt = info.LastWriteTimeUtc,
        };
    }

    /// <summary>Move path.</summary>
    public async Task<ManagedFileStat> MoveAsync(string sourcePath, string destinationPath, bool overwrite, CancellationToken ct)
    {
        var source = await _resolver.ResolvePathAsync(sourcePath, ct).ConfigureAwait(false);
        var destination = await _resolver.ResolvePathAsync(destinationPath, ct).ConfigureAwait(false);

        EnsureExists(source);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(source))
        {
            File.Move(source, destination, overwrite);
        }
        else
        {
            if (Directory.Exists(destination))
            {
                if (!overwrite)
                {
                    throw new IOException($"Target directory already exists: {destination}");
                }

                Directory.Delete(destination, recursive: true);
            }

            Directory.Move(source, destination);
        }

        return await StatAsync(destination, ct).ConfigureAwait(false);
    }

    /// <summary>Copy path.</summary>
    public async Task<ManagedFileStat> CopyAsync(string sourcePath, string destinationPath, bool overwrite, CancellationToken ct)
    {
        var source = await _resolver.ResolvePathAsync(sourcePath, ct).ConfigureAwait(false);
        var destination = await _resolver.ResolvePathAsync(destinationPath, ct).ConfigureAwait(false);

        EnsureExists(source);
        if (File.Exists(source))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite);
            return await StatAsync(destination, ct).ConfigureAwait(false);
        }

        CopyDirectory(source, destination, overwrite);
        return await StatAsync(destination, ct).ConfigureAwait(false);
    }

    /// <summary>Delete path (soft delete or hard delete).</summary>
    public async Task<ManagedDeleteResult> DeleteAsync(string path, bool hardDelete, string requestedBy, CancellationToken ct)
    {
        var fullPath = await _resolver.ResolvePathAsync(path, ct).ConfigureAwait(false);
        EnsureExists(fullPath);
        if (hardDelete)
        {
            DeletePath(fullPath);
            return new ManagedDeleteResult { DeletedPath = fullPath, HardDeleted = true };
        }

        var recycleTarget = await _recycleBin.MoveToRecycleAsync(fullPath, requestedBy, ct).ConfigureAwait(false);
        return new ManagedDeleteResult
        {
            DeletedPath = fullPath,
            HardDeleted = false,
            RecyclePath = recycleTarget,
        };
    }

    /// <summary>Restore softly deleted path.</summary>
    public async Task<ManagedFileStat> RestoreAsync(string recyclePath, string targetPath, CancellationToken ct)
    {
        var destination = await _recycleBin.MoveBackAsync(recyclePath, targetPath, ct).ConfigureAwait(false);
        return await StatAsync(destination, ct).ConfigureAwait(false);
    }

    /// <summary>Query path metadata.</summary>
    public async Task<ManagedFileStat> StatAsync(string path, CancellationToken ct)
    {
        var fullPath = await _resolver.ResolvePathAsync(path, ct).ConfigureAwait(false);
        if (File.Exists(fullPath))
        {
            var file = new FileInfo(fullPath);
            return new ManagedFileStat
            {
                Path = file.FullName,
                Exists = true,
                IsDirectory = false,
                SizeBytes = file.Length,
                ModifiedAt = file.LastWriteTimeUtc,
            };
        }

        if (Directory.Exists(fullPath))
        {
            var directory = new DirectoryInfo(fullPath);
            return new ManagedFileStat
            {
                Path = directory.FullName,
                Exists = true,
                IsDirectory = true,
                SizeBytes = null,
                ModifiedAt = directory.LastWriteTimeUtc,
            };
        }

        return new ManagedFileStat
        {
            Path = fullPath,
            Exists = false,
            IsDirectory = false,
            SizeBytes = null,
            ModifiedAt = null,
        };
    }

    /// <summary>Set Linux permission bits.</summary>
    public async Task ApplyChmodAsync(string path, string mode, CancellationToken ct)
    {
        var fullPath = await _resolver.ResolvePathAsync(path, ct).ConfigureAwait(false);
        EnsureExists(fullPath);
        if (!ModeRegex().IsMatch(mode))
        {
            throw new ArgumentException("chmod mode must be a 3-4 digit octal number.", nameof(mode));
        }

        await ExecuteUnixCommandAsync("chmod", $"{mode} {Quote(fullPath)}", ct).ConfigureAwait(false);
    }

    /// <summary>Set Linux owner.</summary>
    public async Task ApplyChownAsync(string path, string owner, CancellationToken ct)
    {
        var fullPath = await _resolver.ResolvePathAsync(path, ct).ConfigureAwait(false);
        EnsureExists(fullPath);
        if (!OwnerRegex().IsMatch(owner))
        {
            throw new ArgumentException("chown owner format is invalid; should be user or user:group.", nameof(owner));
        }

        await ExecuteUnixCommandAsync("chown", $"{owner} {Quote(fullPath)}", ct).ConfigureAwait(false);
    }

    /// <summary>Computes the SHA-256 ETag of a file (used for conditional upload/read requests).</summary>
    public async Task<string> GetEtagAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(await _resolver.ResolvePathAsync(path, ct).ConfigureAwait(false));
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
    }

    private static byte[] DecodeContent(string content, string encoding)
        => string.Equals(encoding, "base64", StringComparison.OrdinalIgnoreCase)
            ? Convert.FromBase64String(content)
            : Encoding.UTF8.GetBytes(content);

    private const long LegacyWriteDefaultBytes = 1024 * 1024;
    private const long LegacyWriteMinimumBytes = 1;
    private const long LegacyWriteMaximumBytes = 16 * 1024 * 1024;

    private long ReadMaximumLegacyBytes() => Math.Clamp(long.TryParse(_configuration.GetValue("files:legacy_max_bytes"), out var value) ? value : LegacyWriteDefaultBytes, LegacyWriteMinimumBytes, LegacyWriteMaximumBytes);

    private static void DeletePath(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
            return;
        }

        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
            return;
        }

        throw new FileNotFoundException("Path does not exist.", path);
    }

    private static void EnsureExists(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new FileNotFoundException("Path does not exist.", path);
        }
    }

    private static void CopyDirectory(string source, string destination, bool overwrite)
    {
        if (Directory.Exists(destination))
        {
            if (!overwrite)
            {
                throw new IOException($"Target directory already exists: {destination}");
            }

            Directory.Delete(destination, recursive: true);
        }

        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var targetFile = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(file, targetFile, overwrite: true);
        }
    }

    private async Task ExecuteUnixCommandAsync(string executable, string arguments, CancellationToken ct)
    {
        var processManager = _resolver.ProcessManager;
        var result = await processManager.ExecuteCommandAsync(new ProcessStartConfig
        {
            ExecutablePath = executable,
            Arguments = arguments,
            TimeoutSeconds = 30,
        }, ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"{executable} execution failed: {result.Stderr}");
        }
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    [GeneratedRegex("^[0-7]{3,4}$", RegexOptions.CultureInvariant)]
    private static partial Regex ModeRegex();

    [GeneratedRegex("^[A-Za-z0-9_.-]+(:[A-Za-z0-9_.-]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex OwnerRegex();
}

/// <summary>File list entry.</summary>
public sealed record ManagedFileEntry
{
    /// <summary>Full path.</summary>
    public required string Path { get; init; }
    /// <summary>Name.</summary>
    public required string Name { get; init; }
    /// <summary>Is directory.</summary>
    public required bool IsDirectory { get; init; }
    /// <summary>File size; null for directories.</summary>
    public required long? SizeBytes { get; init; }
    /// <summary>Last modified time (UTC).</summary>
    public required DateTime? ModifiedAt { get; init; }
}

/// <summary>File read result.</summary>
public sealed record ManagedFileContent
{
    /// <summary>Path.</summary>
    public required string Path { get; init; }
    /// <summary>Encoding (text/base64).</summary>
    public required string Encoding { get; init; }
    /// <summary>Content.</summary>
    public required string Content { get; init; }
    /// <summary>Raw byte count.</summary>
    public required long SizeBytes { get; init; }
}

/// <summary>Resumable upload session state.</summary>
public sealed record UploadSession(string SessionId, string TargetPath, long ReceivedBytes, long? ExpectedSize, string? ExpectedSha256, string State, DateTimeOffset ExpiresAt, string? Etag, string? TemporaryPath = null);

/// <summary>Client write offset does not match server.</summary>
public sealed class UploadOffsetConflictException(long expectedOffset) : IOException("UPLOAD_OFFSET_CONFLICT")
{
    public long ExpectedOffset { get; } = expectedOffset;
}

/// <summary>Version condition failed on upload completion.</summary>
public sealed class UploadVersionConflictException(string? currentEtag) : IOException("UPLOAD_VERSION_CONFLICT")
{
    public string? CurrentEtag { get; } = currentEtag;
}

/// <summary>File or directory metadata.</summary>
public sealed record ManagedFileStat
{
    /// <summary>Path.</summary>
    public required string Path { get; init; }
    /// <summary>Whether it exists.</summary>
    public required bool Exists { get; init; }
    /// <summary>Is directory.</summary>
    public required bool IsDirectory { get; init; }
    /// <summary>Size; null for directories.</summary>
    public required long? SizeBytes { get; init; }
    /// <summary>Last modified time (UTC).</summary>
    public required DateTime? ModifiedAt { get; init; }
}

/// <summary>Delete result.</summary>
public sealed record ManagedDeleteResult
{
    /// <summary>Deleted target path.</summary>
    public required string DeletedPath { get; init; }
    /// <summary>Whether hard deleted.</summary>
    public required bool HardDeleted { get; init; }
    /// <summary>Recycle bin path after soft delete.</summary>
    public string? RecyclePath { get; init; }
}
