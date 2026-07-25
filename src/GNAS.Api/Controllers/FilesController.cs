using GNAS.Core;
using GNAS.Modules.Share.Services;
using GNAS.Security.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace GNAS.Api.Controllers;

/// <summary>
/// NAS 文件管理控制器。
/// </summary>
[Route("api/files")]
public sealed class FilesController : GnasControllerBase
{
    private readonly FileManagerService _files;
    private readonly ILogPipeline _logs;
    private readonly IPermissionEngine _permissions;

    /// <summary>初始化文件管理控制器。</summary>
    public FilesController(FileManagerService files, ILogPipeline logs, IPermissionEngine permissions)
    {
        _files = files;
        _logs = logs;
        _permissions = permissions;
    }

    /// <summary>列出目录。</summary>
    [HttpGet]
    public Task<IReadOnlyList<ManagedFileEntry>> List([FromQuery] string path, [FromQuery] bool recursive, CancellationToken ct)
        => ExecuteAuditedAsync("files:file:read", "files.list", path, () => _files.ListAsync(path, recursive, ct), ct);

    /// <summary>分页列出目录；保留旧列表响应以兼容现有客户端。</summary>
    [HttpGet("page")]
    public async Task<Page<ManagedFileEntry>> ListPage([FromQuery] string path, [FromQuery] bool recursive, [FromQuery] int offset = 0, [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        var entries = await ExecuteAuditedAsync("files:file:read", "files.list", path, () => _files.ListAsync(path, recursive, ct), ct).ConfigureAwait(false);
        var request = new PageRequest(offset, limit);
        return new Page<ManagedFileEntry>(entries.Skip(request.NormalizedOffset).Take(request.NormalizedLimit).ToArray(), request.NormalizedOffset, request.NormalizedLimit, entries.Count);
    }

    /// <summary>读取路径元数据。</summary>
    [HttpGet("stat")]
    public Task<ManagedFileStat> Stat([FromQuery] string path, CancellationToken ct)
        => ExecuteAuditedAsync("files:file:read", "files.stat", path, () => _files.StatAsync(path, ct), ct);

    /// <summary>读取文件内容。</summary>
    [HttpGet("content")]
    public Task<ManagedFileContent> Read([FromQuery] string path, [FromQuery] string encoding = "text", CancellationToken ct = default)
        => ExecuteAuditedAsync("files:file:read", "files.read", path, () => _files.ReadAsync(path, string.Equals(encoding, "base64", StringComparison.OrdinalIgnoreCase), ct), ct);

    /// <summary>流式下载，支持 ETag 条件请求与 HTTP Range。</summary>
    [HttpGet("download")]
    public async Task<IActionResult> Download([FromQuery] string path, CancellationToken ct)
    {
        var decision = await _permissions.CheckPermissionAsync(OwnerToken, "files:file:read", path, NasDataLevel.Personal, ct).ConfigureAwait(false);
        if (!decision.Granted) throw new PermissionDeniedException(decision.DenyReason);
        var stat = await _files.StatAsync(path, ct).ConfigureAwait(false);
        if (!stat.Exists || stat.IsDirectory) return NotFound();
        var etag = await _files.GetEtagAsync(path, ct).ConfigureAwait(false);
        Response.Headers.ETag = $"\"{etag}\"";
        if (Request.Headers.IfNoneMatch.Any(v => string.Equals(v.ToString().Trim('"'), etag, StringComparison.OrdinalIgnoreCase))) return StatusCode(StatusCodes.Status304NotModified);
        if (Request.Headers.IfMatch.Count > 0 && !Request.Headers.IfMatch.Any(v => string.Equals(v.ToString().Trim('"'), etag, StringComparison.OrdinalIgnoreCase))) return StatusCode(StatusCodes.Status412PreconditionFailed);
        var stream = System.IO.File.OpenRead(stat.Path);
        return new FileStreamResult(stream, "application/octet-stream") { EnableRangeProcessing = true, FileDownloadName = Path.GetFileName(stat.Path) };
    }

    /// <summary>创建可恢复上传会话。</summary>
    [HttpPost("uploads")]
    public async Task<UploadSession> CreateUpload([FromBody] CreateUploadRequest request, CancellationToken ct)
        => await ExecuteAuditedAsync("files:file:write", "files.upload.create", request.Path, () => _files.CreateUploadSessionAsync(request.Path, CurrentSubject, request.SizeBytes, request.Sha256, ct), ct).ConfigureAwait(false);

    /// <summary>追加一个上传分块，Content-Range 必须与服务端 expected offset 一致。</summary>
    [HttpPut("uploads/{sessionId}")]
    public async Task<UploadSession> AppendUpload(string sessionId, CancellationToken ct)
    {
        var range = Request.Headers.ContentRange.ToString();
        if (!TryParseContentRange(range, out var start, out var length)) throw new ArgumentException("Content-Range 格式无效。");
        try { return await _files.AppendUploadAsync(sessionId, CurrentSubject, start, Request.Body, length, ct).ConfigureAwait(false); }
        catch (UploadOffsetConflictException ex) { Response.Headers["Upload-Offset"] = ex.ExpectedOffset.ToString(System.Globalization.CultureInfo.InvariantCulture); throw; }
    }

    /// <summary>原子完成上传，可用 If-Match 防止覆盖新版本。</summary>
    [HttpPost("uploads/{sessionId}/finalize")]
    public Task<ManagedFileStat> FinalizeUpload(string sessionId, CancellationToken ct)
        => _files.FinalizeUploadAsync(sessionId, CurrentSubject, Request.Headers.IfMatch.ToString(), ct);

    /// <summary>查询上传会话状态。</summary>
    [HttpGet("uploads/{sessionId}")]
    public Task<UploadSession> UploadStatus(string sessionId, CancellationToken ct) => _files.GetUploadSessionAsync(sessionId, CurrentSubject, ct);

    /// <summary>中止上传会话并删除临时文件。</summary>
    [HttpDelete("uploads/{sessionId}")]
    public Task AbortUpload(string sessionId, CancellationToken ct) => _files.AbortUploadAsync(sessionId, CurrentSubject, ct);

    /// <summary>创建文件。</summary>
    [HttpPost("write")]
    public Task<ManagedFileStat> Write([FromBody] WriteFileRequest request, CancellationToken ct)
        => ExecuteAuditedAsync("files:file:write", "files.write", request.Path, () => _files.WriteAsync(request.Path, request.Content, request.Encoding ?? "text", request.Overwrite, ct), ct);

    /// <summary>更新文件。</summary>
    [HttpPut("content")]
    public Task<ManagedFileStat> Update([FromBody] UpdateFileRequest request, CancellationToken ct)
        => ExecuteAuditedAsync("files:file:write", "files.update", request.Path, () => _files.WriteAsync(request.Path, request.Content, request.Encoding ?? "text", overwrite: true, ct), ct);

    /// <summary>创建目录。</summary>
    [HttpPost("mkdir")]
    public Task<ManagedFileStat> Mkdir([FromBody] CreateDirectoryRequest request, CancellationToken ct)
        => ExecuteAuditedAsync("files:file:write", "files.mkdir", request.Path, () => _files.CreateDirectoryAsync(request.Path, ct), ct);

    /// <summary>移动路径。</summary>
    [HttpPost("move")]
    public Task<ManagedFileStat> Move([FromBody] MoveFileRequest request, CancellationToken ct)
        => ExecuteAuditedAsync("files:file:write", "files.move", request.SourcePath, () => _files.MoveAsync(request.SourcePath, request.DestinationPath, request.Overwrite, ct), ct);

    /// <summary>复制路径。</summary>
    [HttpPost("copy")]
    public Task<ManagedFileStat> Copy([FromBody] CopyFileRequest request, CancellationToken ct)
        => ExecuteAuditedAsync("files:file:write", "files.copy", request.SourcePath, () => _files.CopyAsync(request.SourcePath, request.DestinationPath, request.Overwrite, ct), ct);

    /// <summary>删除路径，默认软删除。</summary>
    [HttpDelete]
    public Task<ManagedDeleteResult> Delete([FromQuery] string path, [FromQuery] bool hard = false, CancellationToken ct = default)
        => ExecuteAuditedAsync("files:file:delete", "files.delete", path, () => _files.DeleteAsync(path, hard, CurrentSubject, ct), ct);

    /// <summary>恢复软删除路径。</summary>
    [HttpPost("restore")]
    public Task<ManagedFileStat> Restore([FromBody] RestoreFileRequest request, CancellationToken ct)
        => ExecuteAuditedAsync("files:file:delete", "files.restore", request.RecyclePath, () => _files.RestoreAsync(request.RecyclePath, request.TargetPath, ct), ct);

    /// <summary>修改 Linux 权限位。</summary>
    [HttpPost("chmod")]
    public Task<object> Chmod([FromBody] ChmodRequest request, CancellationToken ct)
        => ExecuteAuditedAsync<object>("files:file:admin", "files.chmod", request.Path, async () =>
        {
            await _files.ApplyChmodAsync(request.Path, request.Mode, ct).ConfigureAwait(false);
            return new { success = true, path = request.Path, mode = request.Mode };
        }, ct);

    /// <summary>修改 Linux 所有者。</summary>
    [HttpPost("chown")]
    public Task<object> Chown([FromBody] ChownRequest request, CancellationToken ct)
        => ExecuteAuditedAsync<object>("files:file:admin", "files.chown", request.Path, async () =>
        {
            await _files.ApplyChownAsync(request.Path, request.Owner, ct).ConfigureAwait(false);
            return new { success = true, path = request.Path, owner = request.Owner };
        }, ct);

    private string CurrentSubject
        => (HttpContext.Items["NasTokenPayload"] as NasTokenPayload)?.Sub ?? "unknown";

    private async Task<T> ExecuteAuditedAsync<T>(string requiredCapability, string action, string resource, Func<Task<T>> execute, CancellationToken ct)
    {
        var decision = await _permissions.CheckPermissionAsync(OwnerToken, requiredCapability, resource, NasDataLevel.Personal, ct).ConfigureAwait(false);
        if (!decision.Granted)
        {
            await WriteAuditAsync(action, resource, granted: false, decision.DenyReason, ct).ConfigureAwait(false);
            throw new PermissionDeniedException($"执行 {action} 被拒绝：{decision.DenyReason}");
        }

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

    private Task WriteAuditAsync(string action, string resource, bool granted, string? detail, CancellationToken ct)
        => _logs.ProcessAsync(new LogEntry
        {
            Category = LogCategory.Audit,
            Level = granted ? LogLevel.Information : LogLevel.Warning,
            SourceComponent = "GNAS.Api.FilesController",
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

/// <summary>写入文件请求。</summary>
public sealed record WriteFileRequest(string Path, string Content, string? Encoding, bool Overwrite);
/// <summary>更新文件请求。</summary>
public sealed record UpdateFileRequest(string Path, string Content, string? Encoding);
/// <summary>创建目录请求。</summary>
public sealed record CreateDirectoryRequest(string Path);
/// <summary>移动请求。</summary>
public sealed record MoveFileRequest(string SourcePath, string DestinationPath, bool Overwrite);
/// <summary>复制请求。</summary>
public sealed record CopyFileRequest(string SourcePath, string DestinationPath, bool Overwrite);
/// <summary>恢复请求。</summary>
public sealed record RestoreFileRequest(string RecyclePath, string TargetPath);
/// <summary>chmod 请求。</summary>
public sealed record ChmodRequest(string Path, string Mode);
/// <summary>chown 请求。</summary>
public sealed record ChownRequest(string Path, string Owner);
/// <summary>创建可恢复上传请求。</summary>
public sealed record CreateUploadRequest(string Path, long? SizeBytes, string? Sha256);
