using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using GNAS.Core;

namespace GNAS.Modules.Share.Services;

/// <summary>
/// File management service restricted to the NAS sandbox root directory.
/// </summary>
public sealed partial class FileManagerService
{
    private readonly IGnasConfiguration? _configuration;
    private readonly ShareModule? _shareModule;
    private readonly IProcessManager? _processManager;
    private readonly IDatabaseProvider? _database;

    /// <summary>
    /// Initialize the file management service.
    /// </summary>
    public FileManagerService(IGnasConfiguration? configuration = null, ShareModule? shareModule = null, IProcessManager? processManager = null, IDatabaseProvider? database = null)
    {
        _configuration = configuration;
        _shareModule = shareModule;
        _processManager = processManager;
        _database = database;
    }

    /// <summary>List directory contents.</summary>
    public async Task<IReadOnlyList<ManagedFileEntry>> ListAsync(string path, bool recursive, CancellationToken ct)
    {
        var fullPath = await ResolvePathAsync(path, ct).ConfigureAwait(false);
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
        var fullPath = await ResolvePathAsync(path, ct).ConfigureAwait(false);
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
        var fullPath = await ResolvePathAsync(path, ct).ConfigureAwait(false);
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
        var fullPath = await ResolvePathAsync(path, ct).ConfigureAwait(false);
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
        var source = await ResolvePathAsync(sourcePath, ct).ConfigureAwait(false);
        var destination = await ResolvePathAsync(destinationPath, ct).ConfigureAwait(false);

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
        var source = await ResolvePathAsync(sourcePath, ct).ConfigureAwait(false);
        var destination = await ResolvePathAsync(destinationPath, ct).ConfigureAwait(false);
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
        var fullPath = await ResolvePathAsync(path, ct).ConfigureAwait(false);
        EnsureExists(fullPath);
        if (hardDelete)
        {
            DeletePath(fullPath);
            return new ManagedDeleteResult { DeletedPath = fullPath, HardDeleted = true };
        }

        var recycleTarget = await MoveToRecycleAsync(fullPath, requestedBy, ct).ConfigureAwait(false);
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
        var source = await ResolvePathAsync(recyclePath, ct).ConfigureAwait(false);
        var destination = await ResolvePathAsync(targetPath, ct).ConfigureAwait(false);
        if (!source.Contains($"{Path.DirectorySeparatorChar}.recycle{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            && !source.Contains("/.recycle/", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The restore source path must be located under the .recycle directory.", nameof(recyclePath));
        }

        EnsureExists(source);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(source))
        {
            File.Move(source, destination, overwrite: true);
        }
        else
        {
            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, recursive: true);
            }

            Directory.Move(source, destination);
        }

        return await StatAsync(destination, ct).ConfigureAwait(false);
    }

    /// <summary>Query path metadata.</summary>
    public async Task<ManagedFileStat> StatAsync(string path, CancellationToken ct)
    {
        var fullPath = await ResolvePathAsync(path, ct).ConfigureAwait(false);
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
        var fullPath = await ResolvePathAsync(path, ct).ConfigureAwait(false);
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
        var fullPath = await ResolvePathAsync(path, ct).ConfigureAwait(false);
        EnsureExists(fullPath);
        if (!OwnerRegex().IsMatch(owner))
        {
            throw new ArgumentException("chown owner format is invalid; should be user or user:group.", nameof(owner));
        }

        await ExecuteUnixCommandAsync("chown", $"{owner} {Quote(fullPath)}", ct).ConfigureAwait(false);
    }

    private static byte[] DecodeContent(string content, string encoding)
        => string.Equals(encoding, "base64", StringComparison.OrdinalIgnoreCase)
            ? Convert.FromBase64String(content)
            : Encoding.UTF8.GetBytes(content);

    /// <summary>Create a persistent resumable upload session; the temporary file and target file are in the same directory to ensure atomic replacement.</summary>
    public async Task<UploadSession> CreateUploadSessionAsync(string targetPath, string subject, long? expectedSize, string? expectedSha256, CancellationToken ct)
    {
        var database = RequireDatabase();
        var target = await ResolvePathAsync(targetPath, ct).ConfigureAwait(false);
        if (expectedSize is < 0) throw new ArgumentOutOfRangeException(nameof(expectedSize));
        await database.InitializeAsync(ct).ConfigureAwait(false);
        await CleanupExpiredUploadsAsync(ct).ConfigureAwait(false);
        await using var connection = await database.GetConnectionAsync(ct).ConfigureAwait(false);
        await using (var count = connection.CreateCommand())
        {
            count.CommandText = "SELECT COUNT(*) FROM upload_sessions WHERE subject=$subject AND state='open' AND expires_at > $now;";
            count.Parameters.AddWithValue("$subject", subject);
            count.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            if ((long)(await count.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? 0L) >= 4)
                throw new IOException("UPLOAD_CONCURRENCY_LIMIT");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var id = Guid.CreateVersion7().ToString();
        var temporary = Path.Combine(Path.GetDirectoryName(target)!, $".{Path.GetFileName(target)}.{id}.upload");
        await using (File.Create(temporary)) { }
        var expires = DateTimeOffset.UtcNow.AddHours(24);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO upload_sessions(session_id,subject,target_path,temporary_path,expected_size,expected_sha256,received_bytes,state,expires_at,updated_at) VALUES($id,$subject,$target,$temporary,$size,$sha,0,'open',$expires,$now);";
        command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$subject", subject); command.Parameters.AddWithValue("$target", target); command.Parameters.AddWithValue("$temporary", temporary);
        command.Parameters.AddWithValue("$size", (object?)expectedSize ?? DBNull.Value); command.Parameters.AddWithValue("$sha", (object?)expectedSha256 ?? DBNull.Value);
        command.Parameters.AddWithValue("$expires", expires.ToString("O")); command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return new UploadSession(id, target, 0, expectedSize, expectedSha256, "open", expires, null);
    }

    public async Task<UploadSession> AppendUploadAsync(string sessionId, string subject, long offset, Stream content, long? length, CancellationToken ct)
    {
        var session = await GetUploadSessionAsync(sessionId, subject, ct).ConfigureAwait(false);
        if (session.State != "open") throw new IOException("UPLOAD_SESSION_NOT_OPEN");
        if (session.ReceivedBytes != offset) throw new UploadOffsetConflictException(session.ReceivedBytes);
        await using (var file = new FileStream(session.TemporaryPath!, FileMode.Open, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
        {
            file.Position = offset;
            await content.CopyToAsync(file, ct).ConfigureAwait(false);
            await file.FlushAsync(ct).ConfigureAwait(false);
            file.Flush(flushToDisk: true);
        }
        var received = new FileInfo(session.TemporaryPath!).Length;
        if (length.HasValue && received != offset + length.Value) throw new IOException("UPLOAD_CONTENT_RANGE_INVALID");
        if (session.ExpectedSize.HasValue && received > session.ExpectedSize.Value) throw new IOException("UPLOAD_SIZE_EXCEEDED");
        return await UpdateUploadAsync(session with { ReceivedBytes = received }, ct).ConfigureAwait(false);
    }

    public async Task<ManagedFileStat> FinalizeUploadAsync(string sessionId, string subject, string? ifMatch, CancellationToken ct)
    {
        var session = await GetUploadSessionAsync(sessionId, subject, ct).ConfigureAwait(false);
        if (session.State != "open") throw new IOException("UPLOAD_SESSION_NOT_OPEN");
        if (session.ExpectedSize.HasValue && session.ReceivedBytes != session.ExpectedSize.Value) throw new IOException("UPLOAD_SIZE_MISMATCH");
        string actual;
        await using (var stream = File.OpenRead(session.TemporaryPath!))
        {
            actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
        }
        if (!string.IsNullOrWhiteSpace(session.ExpectedSha256) && !string.Equals(actual, session.ExpectedSha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("UPLOAD_CHECKSUM_MISMATCH");
        var existingEtag = File.Exists(session.TargetPath) ? await GetEtagAsync(session.TargetPath!, ct).ConfigureAwait(false) : null;
        if (!string.IsNullOrWhiteSpace(ifMatch) && !string.Equals(ifMatch.Trim('"'), existingEtag, StringComparison.OrdinalIgnoreCase)) throw new UploadVersionConflictException(existingEtag);
        File.Move(session.TemporaryPath!, session.TargetPath!, overwrite: true);
        await UpdateUploadAsync(session with { State = "completed", Etag = actual }, ct).ConfigureAwait(false);
        return await StatAsync(session.TargetPath!, ct).ConfigureAwait(false);
    }

    public async Task AbortUploadAsync(string sessionId, string subject, CancellationToken ct)
    {
        var session = await GetUploadSessionAsync(sessionId, subject, ct).ConfigureAwait(false);
        if (File.Exists(session.TemporaryPath)) File.Delete(session.TemporaryPath!);
        await UpdateUploadAsync(session with { State = "aborted" }, ct).ConfigureAwait(false);
    }

    public async Task<UploadSession> GetUploadSessionAsync(string sessionId, string subject, CancellationToken ct)
    {
        var database = RequireDatabase();
        await database.InitializeAsync(ct).ConfigureAwait(false);
        await using var connection = await database.GetConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT target_path, temporary_path, expected_size, expected_sha256, received_bytes, state, expires_at, etag FROM upload_sessions WHERE session_id=$id AND subject=$subject;";
        command.Parameters.AddWithValue("$id", sessionId); command.Parameters.AddWithValue("$subject", subject);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false)) throw new FileNotFoundException("UPLOAD_SESSION_NOT_FOUND");
        var expires = DateTimeOffset.Parse(reader.GetString(6), null, System.Globalization.DateTimeStyles.RoundtripKind);
        if (expires <= DateTimeOffset.UtcNow) throw new IOException("UPLOAD_SESSION_EXPIRED");
        return new UploadSession(sessionId, reader.GetString(0), reader.GetInt64(4), reader.IsDBNull(2) ? null : reader.GetInt64(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(5), expires, reader.IsDBNull(7) ? null : reader.GetString(7), reader.GetString(1));
    }

    public async Task<string> GetEtagAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(await ResolvePathAsync(path, ct).ConfigureAwait(false));
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
    }

    private async Task<UploadSession> UpdateUploadAsync(UploadSession session, CancellationToken ct)
    {
        var database = RequireDatabase();
        await using var connection = await database.GetConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE upload_sessions SET received_bytes=$received,state=$state,etag=$etag,updated_at=$updated WHERE session_id=$id;";
        command.Parameters.AddWithValue("$received", session.ReceivedBytes); command.Parameters.AddWithValue("$state", session.State); command.Parameters.AddWithValue("$etag", (object?)session.Etag ?? DBNull.Value);
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O")); command.Parameters.AddWithValue("$id", session.SessionId);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return session;
    }

    private async Task CleanupExpiredUploadsAsync(CancellationToken ct)
    {
        var database = RequireDatabase();
        await using var connection = await database.GetConnectionAsync(ct).ConfigureAwait(false);
        await using var select = connection.CreateCommand();
        select.CommandText = "SELECT temporary_path FROM upload_sessions WHERE expires_at <= $now AND state='open';";
        select.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        var paths = new List<string>();
        await using (var reader = await select.ExecuteReaderAsync(ct).ConfigureAwait(false)) while (await reader.ReadAsync(ct).ConfigureAwait(false)) paths.Add(reader.GetString(0));
        foreach (var path in paths) if (File.Exists(path)) File.Delete(path);
        await using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM upload_sessions WHERE expires_at <= $now;";
        delete.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private IDatabaseProvider RequireDatabase() => _database ?? throw new InvalidOperationException("Upload requires IDatabaseProvider.");
    private long ReadMaximumLegacyBytes() => Math.Clamp(long.TryParse(_configuration?.GetValue("files:legacy_max_bytes"), out var value) ? value : 1024 * 1024, 1, 16 * 1024 * 1024);

    private async Task<string> ResolvePathAsync(string path, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.Contains('\n') || path.Contains('\r'))
        {
            throw new ArgumentException("Path cannot contain newlines.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        var normalizedPath = NormalizePath(fullPath);
        var allowedRoots = await GetAllowedRootsAsync(ct).ConfigureAwait(false);
        if (!allowedRoots.Any(root => IsPathUnderRoot(normalizedPath, root)))
        {
            throw new PermissionDeniedException($"Path exceeds allowed directories: {path}");
        }

        return fullPath;
    }

    private async Task<IReadOnlyList<string>> GetAllowedRootsAsync(CancellationToken ct)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            NormalizePath(GetDataRoot()),
        };
        foreach (var root in ReadConfiguredRoots())
        {
            roots.Add(NormalizePath(root));
        }

        if (_shareModule is not null)
        {
            foreach (var share in await _shareModule.ListSharesAsync(ct).ConfigureAwait(false))
            {
                roots.Add(NormalizePath(Path.GetFullPath(share.Path)));
            }
        }

        return roots.ToArray();
    }

    private string[] ReadConfiguredRoots()
    {
        var values = _configuration?.GetArray("files:allowed_roots") ?? [];
        if (values.Length > 0)
        {
            return values;
        }

        var scalar = _configuration?.GetValue("files:allowed_roots");
        return string.IsNullOrWhiteSpace(scalar)
            ? []
            : scalar.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string GetDataRoot()
    {
        var value = Environment.GetEnvironmentVariable("GNAS_DATA_ROOT");
        return string.IsNullOrWhiteSpace(value) ? "/srv/nas" : value;
    }

    private static string NormalizePath(string path)
        => path.Replace('\\', '/').TrimEnd('/');

    private static bool IsPathUnderRoot(string normalizedPath, string normalizedRoot)
        => string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase)
           || normalizedPath.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase);

    private async Task<string> MoveToRecycleAsync(string path, string requestedBy, CancellationToken ct)
    {
        var root = await ResolveRecycleRootAsync(path, ct).ConfigureAwait(false);
        var user = SanitizeUser(requestedBy);
        var relativePath = Path.GetRelativePath(root, path);
        var target = Path.Combine(root, ".recycle", user, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (File.Exists(path))
        {
            if (File.Exists(target))
            {
                File.Delete(target);
            }

            File.Move(path, target);
            return target;
        }

        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }

        Directory.Move(path, target);
        return target;
    }

    private async Task<string> ResolveRecycleRootAsync(string path, CancellationToken ct)
    {
        var candidates = new List<string>();
        if (_shareModule is not null)
        {
            candidates.AddRange((await _shareModule.ListSharesAsync(ct).ConfigureAwait(false)).Select(s => Path.GetFullPath(s.Path)));
        }

        candidates.Add(GetDataRoot());
        var fullPath = Path.GetFullPath(path);
        var normalizedPath = NormalizePath(fullPath);
        var root = candidates
            .Select(Path.GetFullPath)
            .Select(p => new { Original = p, Normalized = NormalizePath(p) })
            .Where(c => IsPathUnderRoot(normalizedPath, c.Normalized))
            .OrderByDescending(c => c.Normalized.Length)
            .FirstOrDefault();
        if (root is null)
        {
            throw new PermissionDeniedException($"Path is not under a shared directory or the data root directory: {path}");
        }

        return root.Original;
    }

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
        if (_processManager is null)
        {
            throw new InvalidOperationException($"IProcessManager is not registered; cannot execute {executable}.");
        }

        var result = await _processManager.ExecuteCommandAsync(new ProcessStartConfig
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

    private static string SanitizeUser(string requestedBy)
    {
        var value = string.IsNullOrWhiteSpace(requestedBy) ? "unknown" : requestedBy;
        value = value.Replace('\\', '_').Replace('/', '_').Replace(':', '_');
        return value;
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
