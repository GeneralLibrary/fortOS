using System.Security.Cryptography;
using FortOS.Core;
using Microsoft.Data.Sqlite;

namespace FortOS.Modules.Share.Services;

/// <summary>
/// Persistent resumable-upload sessions: tracks temporary files and metadata in the
/// upload_sessions table and enforces concurrency/expiry limits. Split out of
/// FileManagerService so the file CRUD class does not own database session state.
/// </summary>
public sealed class UploadSessionService
{
    /// <summary>Maximum number of open upload sessions a single subject may hold concurrently.</summary>
    internal const int MaxSessionsPerSubject = 4;
    /// <summary>How long an open upload session may remain idle before it is cleaned up.</summary>
    internal static readonly TimeSpan SessionExpiry = TimeSpan.FromHours(24);

    private readonly FilePathResolver _resolver;
    private readonly IDatabaseProvider _database;
    private readonly FileManagerService _files;

    /// <summary>
    /// Initialize the upload session service.
    /// </summary>
    /// <param name="resolver">Path validation.</param>
    /// <param name="database">SQLite provider for session state.</param>
    /// <param name="files">File service used to finalize (stat/etag) completed uploads.</param>
    public UploadSessionService(FilePathResolver resolver, IDatabaseProvider database, FileManagerService files)
    {
        _resolver = resolver;
        _database = database;
        _files = files;
    }

    /// <summary>Create a persistent resumable upload session; the temporary file and target file are in the same directory to ensure atomic replacement.</summary>
    public async Task<UploadSession> CreateUploadSessionAsync(string targetPath, string subject, long? expectedSize, string? expectedSha256, CancellationToken ct)
    {
        var database = RequireDatabase();
        var target = await _resolver.ResolvePathAsync(targetPath, ct).ConfigureAwait(false);
        if (expectedSize is < 0) throw new ArgumentOutOfRangeException(nameof(expectedSize));
        await database.InitializeAsync(ct).ConfigureAwait(false);
        await CleanupExpiredUploadsAsync(ct).ConfigureAwait(false);
        await using var connection = await database.GetConnectionAsync(ct).ConfigureAwait(false);
        await using (var count = connection.CreateCommand())
        {
            count.CommandText = "SELECT COUNT(*) FROM upload_sessions WHERE subject=$subject AND state='open' AND expires_at > $now;";
            count.Parameters.AddWithValue("$subject", subject);
            count.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            if ((long)(await count.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? 0L) >= MaxSessionsPerSubject)
                throw new IOException("UPLOAD_CONCURRENCY_LIMIT");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var id = Guid.CreateVersion7().ToString();
        var temporary = Path.Combine(Path.GetDirectoryName(target)!, $".{Path.GetFileName(target)}.{id}.upload");
        await using (File.Create(temporary)) { }
        var expires = DateTimeOffset.UtcNow.Add(SessionExpiry);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO upload_sessions(session_id,subject,target_path,temporary_path,expected_size,expected_sha256,received_bytes,state,expires_at,updated_at) VALUES($id,$subject,$target,$temporary,$size,$sha,0,'open',$expires,$now);";
        command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$subject", subject); command.Parameters.AddWithValue("$target", target); command.Parameters.AddWithValue("$temporary", temporary);
        command.Parameters.AddWithValue("$size", (object?)expectedSize ?? DBNull.Value); command.Parameters.AddWithValue("$sha", (object?)expectedSha256 ?? DBNull.Value);
        command.Parameters.AddWithValue("$expires", expires.ToString("O")); command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return new UploadSession(id, target, 0, expectedSize, expectedSha256, "open", expires, null);
    }

    /// <summary>Append a chunk to an open upload session, verifying the client offset matches the server state.</summary>
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

    /// <summary>Atomically complete an upload session: verifies size/checksum/If-Match, then renames the temporary file over the target.</summary>
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
        var existingEtag = File.Exists(session.TargetPath) ? await _files.GetEtagAsync(session.TargetPath!, ct).ConfigureAwait(false) : null;
        if (!string.IsNullOrWhiteSpace(ifMatch) && !string.Equals(ifMatch.Trim('"'), existingEtag, StringComparison.OrdinalIgnoreCase)) throw new UploadVersionConflictException(existingEtag);
        File.Move(session.TemporaryPath!, session.TargetPath!, overwrite: true);
        await UpdateUploadAsync(session with { State = "completed", Etag = actual }, ct).ConfigureAwait(false);
        return await _files.StatAsync(session.TargetPath!, ct).ConfigureAwait(false);
    }

    /// <summary>Abort an upload session and delete its temporary file.</summary>
    public async Task AbortUploadAsync(string sessionId, string subject, CancellationToken ct)
    {
        var session = await GetUploadSessionAsync(sessionId, subject, ct).ConfigureAwait(false);
        if (File.Exists(session.TemporaryPath)) File.Delete(session.TemporaryPath!);
        await UpdateUploadAsync(session with { State = "aborted" }, ct).ConfigureAwait(false);
    }

    /// <summary>Returns the current state of a session owned by <paramref name="subject"/>; throws when missing or expired.</summary>
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

    private IDatabaseProvider RequireDatabase() => _database;
}
