using FortOS.Core;
using FortOS.Modules.Share.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using FortOS.Api.Middleware;

namespace FortOS.Api.Filters;

/// <summary>Unified exception response filter.</summary>
public sealed class FortOSExceptionFilter : IExceptionFilter
{
    private readonly ILogger<FortOSExceptionFilter> logger;

    /// <summary>Initializes the exception filter.</summary>
    public FortOSExceptionFilter(ILogger<FortOSExceptionFilter> logger) => this.logger = logger;

    /// <inheritdoc />
    public void OnException(ExceptionContext context)
    {
        var traceId = context.HttpContext.Items["X-Trace-Id"]?.ToString();
        var (status, code, error) = Map(context.Exception);
        if (status >= 500) logger.LogError(context.Exception, "API request failed.");
        var problem = new ProblemDetails
        {
            Status = status,
            Title = status >= 500 ? "Internal server error" : "Request failed",
            Detail = error,
            Type = $"https://fortos.dev/problems/{code.ToLowerInvariant()}"
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = traceId ?? context.HttpContext.TraceIdentifier;
        context.Result = new ObjectResult(problem) { StatusCode = status };
        context.ExceptionHandled = true;
    }

    internal static (int Status, string Code, string Error) Map(Exception exception) => exception switch
    {
        ServiceNotFoundException ex => (StatusCodes.Status404NotFound, ex.ErrorCode, ex.Message),
        PermissionDeniedException ex => (StatusCodes.Status403Forbidden, ex.ErrorCode, ex.Message),
        TokenValidationException ex => (StatusCodes.Status401Unauthorized, ex.ErrorCode, ex.Message),
        ConfigurationException ex => (StatusCodes.Status400BadRequest, ex.ErrorCode, ex.Message),
        // Upload resume conflicts are client-correctable, so they must be 4xx (409),
        // not the generic 500 an unhandled IOException would produce. The controller
        // additionally sets the Upload-Offset header before rethrowing.
        UploadOffsetConflictException ex => (StatusCodes.Status409Conflict, "UPLOAD_OFFSET_CONFLICT", ex.Message),
        UploadVersionConflictException ex => (StatusCodes.Status412PreconditionFailed, "UPLOAD_VERSION_CONFLICT", ex.Message),
        // Missing files/directories are client-correctable conditions and must surface as
        // 404 (matching FilesController.Download), not as generic 500 internal errors.
        FileNotFoundException ex => (StatusCodes.Status404NotFound, "FILE_NOT_FOUND", ex.Message),
        DirectoryNotFoundException ex => (StatusCodes.Status404NotFound, "DIRECTORY_NOT_FOUND", ex.Message),
        ArgumentException ex => (StatusCodes.Status400BadRequest, "INVALID_ARGUMENT", ex.Message),
        // Command failures (mkfs, mount, mdadm, ...) carry the process's stderr,
        // which is what makes the failure actionable for the user — e.g. a RAID
        // format/mount failing because the mount point does not exist (issue #16).
        // Must be matched before FortOSException since it derives from it.
        CommandExecutionException ex => (StatusCodes.Status500InternalServerError, "COMMAND_EXECUTION_FAILED", CommandDetail(ex)),
        FortOSException ex => (StatusCodes.Status500InternalServerError, ex.ErrorCode, ex.Message),
        _ => (StatusCodes.Status500InternalServerError, "INTERNAL_ERROR", "Internal server error."),
    };

    /// <summary>Appends the failing command's stderr (truncated) to the exception message so the UI shows the actual reason.</summary>
    internal static string CommandDetail(CommandExecutionException ex)
    {
        var stderr = string.IsNullOrWhiteSpace(ex.Stderr) ? string.Empty : " — " + ex.Stderr.Trim();
        var detail = ex.Message + stderr;
        return detail.Length <= 4000 ? detail : detail[..4000];
    }
}
