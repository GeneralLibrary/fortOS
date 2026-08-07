using FortOS.Api.Authorization;
using FortOS.Core;
using FortOS.Modules.Share.Services;
using FortOS.Security.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace FortOS.Api.Controllers;

/// <summary>
/// NAS file management controller.
/// </summary>
[Route("api/files")]
public sealed class FilesController : FortOSControllerBase
{
    private readonly FileManagerService _files;
    private readonly UploadSessionService _uploads;
    private readonly ILogPipeline _logs;
    private readonly IPermissionEngine _permissions;

    /// <summary>Initializes the file management controller.</summary>
    public FilesController(FileManagerService files, UploadSessionService uploads, ILogPipeline logs, IPermissionEngine permissions)
    {
        _files = files;
        _uploads = uploads;
        _logs = logs;
        _permissions = permissions;
    }

    /// <summary>List directory.</summary>
    [RequiresCapability("files:file:read", NasDataLevel.Personal)]
    [HttpGet]
    public Task<IReadOnlyList<ManagedFileEntry>> List([FromQuery] string path, [FromQuery] bool recursive, CancellationToken ct)
        => ExecuteAuditedAsync("files:file:read", "files.list", path, () => _files.ListAsync(path, recursive, ct), ct);

    /// <summary>Paginated directory listing; keeps the legacy list response for client compatibility.</summary>
    [RequiresCapability("files:file:read", NasDataLevel.Personal)]
    [HttpGet("page")]
    public async Task<Page<ManagedFileEntry>> ListPage([FromQuery] string path, [FromQuery] bool recursive, [FromQuery] int offset = 0, [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        var entries = await ExecuteAuditedAsync("files:file:read", "files.list", path, () => _files.ListAsync(path, recursive, ct), ct).ConfigureAwait(false);
        var request = new PageRequest(offset, limit);
        return new Page<ManagedFileEntry>(entries.Skip(request.NormalizedOffset).Take(request.NormalizedLimit).ToArray(), request.NormalizedOffset, request.NormalizedLimit, entries.Count);
    }

    /// <summary>Read path metadata.</summary>
    [RequiresCapability("files:file:read", NasDataLevel.Personal)]
    [HttpGet("stat")]
    public Task<ManagedFileStat> Stat([FromQuery] string path, CancellationToken ct)
        => ExecuteAuditedAsync("files:file:read", "files.stat", path, () => _files.StatAsync(path, ct), ct);

    /// <summary>Read file content.</summary>
    [RequiresCapability("files:file:read", NasDataLevel.Personal)]
    [HttpGet("content")]
    public Task<ManagedFileContent> Read([FromQuery] string path, [FromQuery] string encoding = "text", CancellationToken ct = default)
        => ExecuteAuditedAsync("files:file:read", "files.read", path, () => _files.ReadAsync(path, string.Equals(encoding, "base64", StringComparison.OrdinalIgnoreCase), ct), ct);

    /// <summary>Streaming download, supports ETag conditional requests and HTTP Range.</summary>
    [RequiresCapability("files:file:read", NasDataLevel.Personal)]
    [HttpGet("download")]
    public async Task<IActionResult> Download([FromQuery] string path, CancellationToken ct)
    {
        await EnsureAuthorizedAsync("files:file:read", "files.download", path, ct).ConfigureAwait(false);
        var stat = await _files.StatAsync(path, ct).ConfigureAwait(false);
        if (!stat.Exists || stat.IsDirectory) return NotFound();
        var etag = await _files.GetEtagAsync(path, ct).ConfigureAwait(false);
        Response.Headers.ETag = $"\"{etag}\"";
        if (Request.Headers.IfNoneMatch.Any(v => v is not null && string.Equals(v.Trim('"'), etag, StringComparison.OrdinalIgnoreCase))) return StatusCode(StatusCodes.Status304NotModified);
        if (Request.Headers.IfMatch.Count > 0 && !Request.Headers.IfMatch.Any(v => v is not null && string.Equals(v.Trim('"'), etag, StringComparison.OrdinalIgnoreCase))) return StatusCode(StatusCodes.Status412PreconditionFailed);
        var stream = System.IO.File.OpenRead(stat.Path);
        return new FileStreamResult(stream, "application/octet-stream") { EnableRangeProcessing = true, FileDownloadName = Path.GetFileName(stat.Path) };
    }

    /// <summary>Create resumable upload session.</summary>
    [RequiresCapability("files:file:write", NasDataLevel.Personal)]
    [HttpPost("uploads")]
    public async Task<UploadSession> CreateUpload([FromBody] CreateUploadRequest request, CancellationToken ct)
        => await ExecuteAuditedAsync("files:file:write", "files.upload.create", request.Path, () => _uploads.CreateUploadSessionAsync(request.Path, CurrentSubject, request.SizeBytes, request.Sha256, ct), ct).ConfigureAwait(false);

    /// <summary>Append an upload chunk; Content-Range must match the server's expected offset.</summary>
    [RequiresCapability("files:file:write", NasDataLevel.Personal)]
    [HttpPut("uploads/{sessionId}")]
    public async Task<UploadSession> AppendUpload(string sessionId, CancellationToken ct)
    {
        var range = Request.Headers.ContentRange.ToString();
        if (!TryParseContentRange(range, out var start, out var length)) throw new ArgumentException("Invalid Content-Range format.");
        try { return await _uploads.AppendUploadAsync(sessionId, CurrentSubject, start, Request.Body, length, ct).ConfigureAwait(false); }
        catch (UploadOffsetConflictException ex) { Response.Headers["Upload-Offset"] = ex.ExpectedOffset.ToString(System.Globalization.CultureInfo.InvariantCulture); throw; }
    }

    /// <summary>Atomically complete upload; use If-Match to prevent overwriting a newer version.</summary>
    [RequiresCapability("files:file:write", NasDataLevel.Personal)]
    [HttpPost("uploads/{sessionId}/finalize")]
    public Task<ManagedFileStat> FinalizeUpload(string sessionId, CancellationToken ct)
        => _uploads.FinalizeUploadAsync(sessionId, CurrentSubject, Request.Headers.IfMatch.ToString(), ct);

    /// <summary>Query upload session status.</summary>
    [RequiresCapability("files:file:read", NasDataLevel.Personal)]
    [HttpGet("uploads/{sessionId}")]
    public Task<UploadSession> UploadStatus(string sessionId, CancellationToken ct) => _uploads.GetUploadSessionAsync(sessionId, CurrentSubject, ct);

    /// <summary>Abort upload session and delete temporary files.</summary>
    [RequiresCapability("files:file:write", NasDataLevel.Personal)]
    [HttpDelete("uploads/{sessionId}")]
    public Task AbortUpload(string sessionId, CancellationToken ct) => _uploads.AbortUploadAsync(sessionId, CurrentSubject, ct);

    /// <summary>Create file.</summary>
    [RequiresCapability("files:file:write", NasDataLevel.Personal)]
    [HttpPost("write")]
    public Task<ManagedFileStat> Write([FromBody] WriteFileRequest request, CancellationToken ct)
        => ExecuteAuditedAsync("files:file:write", "files.write", request.Path, () => _files.WriteAsync(request.Path, request.Content, request.Encoding ?? "text", request.Overwrite, ct), ct);

    /// <summary>Update file.</summary>
    [RequiresCapability("files:file:write", NasDataLevel.Personal)]
    [HttpPut("content")]
    public Task<ManagedFileStat> Update([FromBody] UpdateFileRequest request, CancellationToken ct)
        => ExecuteAuditedAsync("files:file:write", "files.update", request.Path, () => _files.WriteAsync(request.Path, request.Content, request.Encoding ?? "text", overwrite: true, ct), ct);

    /// <summary>Create directory.</summary>
    [RequiresCapability("files:file:write", NasDataLevel.Personal)]
    [HttpPost("mkdir")]
    public Task<ManagedFileStat> Mkdir([FromBody] CreateDirectoryRequest request, CancellationToken ct)
        => ExecuteAuditedAsync("files:file:write", "files.mkdir", request.Path, () => _files.CreateDirectoryAsync(request.Path, ct), ct);

    /// <summary>Move path.</summary>
    [RequiresCapability("files:file:write", NasDataLevel.Personal)]
    [HttpPost("move")]
    public Task<ManagedFileStat> Move([FromBody] MoveFileRequest request, CancellationToken ct)
        => ExecuteAuditedAsync("files:file:write", "files.move", request.SourcePath, () => _files.MoveAsync(request.SourcePath, request.DestinationPath, request.Overwrite, ct), ct);

    /// <summary>Copy path.</summary>
    [RequiresCapability("files:file:write", NasDataLevel.Personal)]
    [HttpPost("copy")]
    public Task<ManagedFileStat> Copy([FromBody] CopyFileRequest request, CancellationToken ct)
        => ExecuteAuditedAsync("files:file:write", "files.copy", request.SourcePath, () => _files.CopyAsync(request.SourcePath, request.DestinationPath, request.Overwrite, ct), ct);

    /// <summary>Delete path, soft delete by default.</summary>
    [RequiresCapability("files:file:delete", NasDataLevel.Personal)]
    [HttpDelete]
    public Task<ManagedDeleteResult> Delete([FromQuery] string path, [FromQuery] bool hard = false, CancellationToken ct = default)
        => ExecuteAuditedAsync("files:file:delete", "files.delete", path, () => _files.DeleteAsync(path, hard, CurrentSubject, ct), ct);

    /// <summary>Restore soft-deleted path.</summary>
    [RequiresCapability("files:file:delete", NasDataLevel.Personal)]
    [HttpPost("restore")]
    public Task<ManagedFileStat> Restore([FromBody] RestoreFileRequest request, CancellationToken ct)
        => ExecuteAuditedAsync("files:file:delete", "files.restore", request.RecyclePath, () => _files.RestoreAsync(request.RecyclePath, request.TargetPath, ct), ct);

    /// <summary>Modify Linux permission bits.</summary>
    [RequiresCapability("files:file:admin", NasDataLevel.Personal)]
    [HttpPost("chmod")]
    public Task<ChmodResult> Chmod([FromBody] ChmodRequest request, CancellationToken ct)
        => ExecuteAuditedAsync("files:file:admin", "files.chmod", request.Path, async () =>
        {
            await _files.ApplyChmodAsync(request.Path, request.Mode, ct).ConfigureAwait(false);
            return new ChmodResult(request.Path, request.Mode);
        }, ct);

    /// <summary>Modify Linux owner.</summary>
    [RequiresCapability("files:file:admin", NasDataLevel.Personal)]
    [HttpPost("chown")]
    public Task<ChownResult> Chown([FromBody] ChownRequest request, CancellationToken ct)
        => ExecuteAuditedAsync("files:file:admin", "files.chown", request.Path, async () =>
        {
            await _files.ApplyChownAsync(request.Path, request.Owner, ct).ConfigureAwait(false);
            return new ChownResult(request.Path, request.Owner);
        }, ct);

    private string CurrentSubject
        => (HttpContext.Items["NasTokenPayload"] as NasTokenPayload)?.Sub ?? "unknown";

    private async Task<T> ExecuteAuditedAsync<T>(string requiredCapability, string action, string resource, Func<Task<T>> execute, CancellationToken ct)
    {
        await EnsureAuthorizedAsync(requiredCapability, action, resource, ct).ConfigureAwait(false);

        try
        {
            var result = await execute().ConfigureAwait(false);
            await WriteAuditAsync(action, resource, granted: true, null, ct).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            await WriteAuditAsync(action, resource, granted: false, ex.Message, ct).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Enforces capability authorization for the current request. The central
    /// CapabilityAuthorizationFilter already enforces authorization; when authentication is
    /// disabled (development/evaluation mode) it lets every request through, so the in-controller
    /// check must mirror that instead of rejecting token-less requests (OwnerToken would be empty).
    /// </summary>
    private async Task EnsureAuthorizedAsync(string requiredCapability, string action, string resource, CancellationToken ct)
    {
        var configuration = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        if (configuration.GetValue("security:require_auth", true))
        {
            var decision = await _permissions.CheckPermissionAsync(OwnerToken, requiredCapability, resource, NasDataLevel.Personal, ct).ConfigureAwait(false);
            if (!decision.Granted)
            {
                await WriteAuditAsync(action, resource, granted: false, decision.DenyReason, ct).ConfigureAwait(false);
                throw new PermissionDeniedException($"Execution of {action} was denied: {decision.DenyReason}");
            }
        }
    }

    private Task WriteAuditAsync(string action, string resource, bool granted, string? detail, CancellationToken ct)
        => _logs.ProcessAsync(new LogEntry
        {
            Category = LogCategory.Audit,
            Level = granted ? LogLevel.Information : LogLevel.Warning,
            SourceComponent = "FortOS.Api.FilesController",
            UserId = CurrentSubject,
            Message = granted
                ? $"File operation succeeded: {action} {resource}"
                : $"File operation failed: {action} {resource} {detail}",
            TraceId = TraceId,
            Audit = new AuditDetail
            {
                Action = action,
                Resource = resource,
                ResourceType = "file",
                Granted = granted,
                CurrentHash = string.Empty,
                ChainSignature = string.Empty,
            },
        }, ct);

    private static bool TryParseContentRange(string value, out long start, out long length)
    {
        start = length = 0;
        if (!value.StartsWith("bytes ", StringComparison.OrdinalIgnoreCase)) return false;
        var range = value[6..].Split('/');
        var bounds = range[0].Split('-');
        if (bounds.Length != 2 || !long.TryParse(bounds[0], out start) || !long.TryParse(bounds[1], out var end) || end < start) return false;
        length = end - start + 1;
        return true;
    }
}

/// <summary>Write file request.</summary>
public sealed record WriteFileRequest(string Path, string Content, string? Encoding, bool Overwrite);
/// <summary>Update file request.</summary>
public sealed record UpdateFileRequest(string Path, string Content, string? Encoding);
/// <summary>Create directory request.</summary>
public sealed record CreateDirectoryRequest(string Path);
/// <summary>Move request.</summary>
public sealed record MoveFileRequest(string SourcePath, string DestinationPath, bool Overwrite);
/// <summary>Copy request.</summary>
public sealed record CopyFileRequest(string SourcePath, string DestinationPath, bool Overwrite);
/// <summary>Restore request.</summary>
public sealed record RestoreFileRequest(string RecyclePath, string TargetPath);
/// <summary>Chmod request.</summary>
public sealed record ChmodRequest(string Path, string Mode);
/// <summary>Chown request.</summary>
public sealed record ChownRequest(string Path, string Owner);
/// <summary>Chmod result.</summary>
public sealed record ChmodResult(string Path, string Mode);
/// <summary>Chown result.</summary>
public sealed record ChownResult(string Path, string Owner);
/// <summary>Create resumable upload request.</summary>
public sealed record CreateUploadRequest(string Path, long? SizeBytes, string? Sha256);
