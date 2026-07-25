using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using GNAS.Core;

namespace GNAS.Modules.Share.Services;

/// <summary>
/// 受限于 NAS 沙箱根目录的文件管理服务。
/// </summary>
public sealed partial class FileManagerService
{
    private readonly IGnasConfiguration? _configuration;
    private readonly ShareModule? _shareModule;
    private readonly IProcessManager? _processManager;
    private readonly IDatabaseProvider? _database;

    /// <summary>
    /// 初始化文件管理服务。
    /// </summary>
    public FileManagerService(IGnasConfiguration? configuration = null, ShareModule? shareModule = null, IProcessManager? processManager = null, IDatabaseProvider? database = null)
    {
        _configuration = configuration;
        _shareModule = shareModule;
        _processManager = processManager;
        _database = database;
    }

    /// <summary>列出目录。</summary>
    public async Task<IReadOnlyList<ManagedFileEntry>> ListAsync(string path, bool recursive, CancellationToken ct)
    {
        var fullPath = await ResolvePathAsync(path, ct).ConfigureAwait(false);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"目录不存在：{fullPath}");
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

    /// <summary>读取文件内容。</summary>
    public async Task<ManagedFileContent> ReadAsync(string path, bool asBase64, CancellationToken ct)
    {
        var fullPath = await ResolvePathAsync(path, ct).ConfigureAwait(false);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("文件不存在。", fullPath);
        }

        var max = ReadMaximumLegacyBytes();
        var info = new FileInfo(fullPath);
        if (info.Length > max) throw new IOException($"旧 JSON 内容接口最多支持 {max} 字节；请使用流式下载。");
        var bytes = await File.ReadAllBytesAsync(fullPath, ct).ConfigureAwait(false);
        return asBase64
            ? new ManagedFileContent { Path = fullPath, Encoding = "base64", Content = Convert.ToBase64String(bytes), SizeBytes = bytes.LongLength }
            : new ManagedFileContent { Path = fullPath, Encoding = "text", Content = Encoding.UTF8.GetString(bytes), SizeBytes = bytes.LongLength };
    }

    /// <summary>写入文件。</summary>
    public async Task<ManagedFileStat> WriteAsync(string path, string content, string encoding, bool overwrite, CancellationToken ct)
    {
        var fullPath = await ResolvePathAsync(path, ct).ConfigureAwait(false);
        if (File.Exists(fullPath) && !overwrite)
        {
            throw new IOException($"文件已存在：{fullPath}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        if (Encoding.UTF8.GetByteCount(content) > ReadMaximumLegacyBytes()) throw new IOException("旧 JSON/base64 写入接口的内容超出限制；请使用可恢复上传。");
        var bytes = DecodeContent(content, encoding);
        await File.WriteAllBytesAsync(fullPath, bytes, ct).ConfigureAwait(false);
        return await StatAsync(fullPath, ct).ConfigureAwait(false);
    }

    /// <summary>创建目录。</summary>
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

    /// <summary>移动路径。</summary>
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
                    throw new IOException($"目标目录已存在：{destination}");
                }

                Directory.Delete(destination, recursive: true);
            }

            Directory.Move(source, destination);
        }

        return await StatAsync(destination, ct).ConfigureAwait(false);
    }

    /// <summary>复制路径。</summary>
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

    /// <summary>删除路径（软删或硬删）。</summary>
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

    /// <summary>恢复软删路径。</summary>
    public async Task<ManagedFileStat> RestoreAsync(string recyclePath, string targetPath, CancellationToken ct)
    {
        var source = await ResolvePathAsync(recyclePath, ct).ConfigureAwait(false);
        var destination = await ResolvePathAsync(targetPath, ct).ConfigureAwait(false);
        if (!source.Contains($"{Path.DirectorySeparatorChar}.recycle{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            && !source.Contains("/.recycle/", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("恢复源路径必须位于 .recycle 目录下。", nameof(recyclePath));
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

    /// <summary>查询路径元数据。</summary>
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

    /// <summary>设置 Linux 权限位。</summary>
    public async Task ApplyChmodAsync(string path, string mode, CancellationToken ct)
    {
        var fullPath = await ResolvePathAsync(path, ct).ConfigureAwait(false);
        EnsureExists(fullPath);
        if (!ModeRegex().IsMatch(mode))
        {
            throw new ArgumentException("chmod 模式必须是 3-4 位八进制数字。", nameof(mode));
        }

        await ExecuteUnixCommandAsync("chmod", $"{mode} {Quote(fullPath)}", ct).ConfigureAwait(false);
    }

    /// <summary>设置 Linux 所有者。</summary>
    public async Task ApplyChownAsync(string path, string owner, CancellationToken ct)
    {
        var fullPath = await ResolvePathAsync(path, ct).ConfigureAwait(false);
        EnsureExists(fullPath);
        if (!OwnerRegex().IsMatch(owner))
        {
            throw new ArgumentException("chown owner 格式非法，应为 user 或 user:group。", nameof(owner));
        }

        await ExecuteUnixCommandAsync("chown", $"{owner} {Quote(fullPath)}", ct).ConfigureAwait(false);
    }

    private static byte[] DecodeContent(string content, string encoding)
        => string.Equals(encoding, "base64", StringComparison.OrdinalIgnoreCase)
            ? Convert.FromBase64String(content)
            : Encoding.UTF8.GetBytes(content);

    /// <summary>创建持久化可恢复上传会话，临时文件与目标文件位于同一目录以保证替换原子性。</summary>
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

    private IDatabaseProvider RequireDatabase() => _database ?? throw new InvalidOperationException("上传需要 IDatabaseProvider。");
    private long ReadMaximumLegacyBytes() => Math.Clamp(long.TryParse(_configuration?.GetValue("files:legacy_max_bytes"), out var value) ? value : 1024 * 1024, 1, 16 * 1024 * 1024);

    private async Task<string> ResolvePathAsync(string path, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.Contains('\n') || path.Contains('\r'))
        {
            throw new ArgumentException("路径不能包含换行。", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        var normalizedPath = NormalizePath(fullPath);
        var allowedRoots = await GetAllowedRootsAsync(ct).ConfigureAwait(false);
        if (!allowedRoots.Any(root => IsPathUnderRoot(normalizedPath, root)))
        {
            throw new PermissionDeniedException($"路径超出允许目录：{path}");
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
            throw new PermissionDeniedException($"路径不在共享目录或数据根目录下：{path}");
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

        throw new FileNotFoundException("路径不存在。", path);
    }

    private static void EnsureExists(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new FileNotFoundException("路径不存在。", path);
        }
    }

    private static void CopyDirectory(string source, string destination, bool overwrite)
    {
        if (Directory.Exists(destination))
        {
            if (!overwrite)
            {
                throw new IOException($"目标目录已存在：{destination}");
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
            throw new InvalidOperationException($"未注册 IProcessManager，无法执行 {executable}。");
        }

        if (OperatingSystem.IsWindows())
        {
            throw new PlatformException($"{executable} 仅在 Linux 平台受支持。");
        }

        var result = await _processManager.ExecuteCommandAsync(new ProcessStartConfig
        {
            ExecutablePath = executable,
            Arguments = arguments,
            TimeoutSeconds = 30,
        }, ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"{executable} 执行失败：{result.Stderr}");
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

/// <summary>文件列表项。</summary>
public sealed record ManagedFileEntry
{
    /// <summary>完整路径。</summary>
    public required string Path { get; init; }
    /// <summary>名称。</summary>
    public required string Name { get; init; }
    /// <summary>是否目录。</summary>
    public required bool IsDirectory { get; init; }
    /// <summary>文件大小；目录时为空。</summary>
    public required long? SizeBytes { get; init; }
    /// <summary>最后修改时间（UTC）。</summary>
    public required DateTime? ModifiedAt { get; init; }
}

/// <summary>文件读取结果。</summary>
public sealed record ManagedFileContent
{
    /// <summary>路径。</summary>
    public required string Path { get; init; }
    /// <summary>编码（text/base64）。</summary>
    public required string Encoding { get; init; }
    /// <summary>内容。</summary>
    public required string Content { get; init; }
    /// <summary>原始字节数。</summary>
    public required long SizeBytes { get; init; }
}

/// <summary>可恢复上传会话状态。</summary>
public sealed record UploadSession(string SessionId, string TargetPath, long ReceivedBytes, long? ExpectedSize, string? ExpectedSha256, string State, DateTimeOffset ExpiresAt, string? Etag, string? TemporaryPath = null);

/// <summary>客户端写入偏移与服务端不一致。</summary>
public sealed class UploadOffsetConflictException(long expectedOffset) : IOException("UPLOAD_OFFSET_CONFLICT")
{
    public long ExpectedOffset { get; } = expectedOffset;
}

/// <summary>上传完成时的版本条件失败。</summary>
public sealed class UploadVersionConflictException(string? currentEtag) : IOException("UPLOAD_VERSION_CONFLICT")
{
    public string? CurrentEtag { get; } = currentEtag;
}

/// <summary>文件或目录元数据。</summary>
public sealed record ManagedFileStat
{
    /// <summary>路径。</summary>
    public required string Path { get; init; }
    /// <summary>是否存在。</summary>
    public required bool Exists { get; init; }
    /// <summary>是否目录。</summary>
    public required bool IsDirectory { get; init; }
    /// <summary>大小；目录时为空。</summary>
    public required long? SizeBytes { get; init; }
    /// <summary>最后修改时间（UTC）。</summary>
    public required DateTime? ModifiedAt { get; init; }
}

/// <summary>删除结果。</summary>
public sealed record ManagedDeleteResult
{
    /// <summary>删除目标路径。</summary>
    public required string DeletedPath { get; init; }
    /// <summary>是否硬删除。</summary>
    public required bool HardDeleted { get; init; }
    /// <summary>软删除后的回收站路径。</summary>
    public string? RecyclePath { get; init; }
}
