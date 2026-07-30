using GORT.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using GORT.Api.Middleware;

namespace GORT.Api.Filters;

/// <summary>Unified exception response filter.</summary>
public sealed class GortExceptionFilter : IExceptionFilter
{
    private readonly ILogger<GortExceptionFilter> logger;

    /// <summary>Initializes the exception filter.</summary>
    public GortExceptionFilter(ILogger<GortExceptionFilter> logger) => this.logger = logger;

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
            Type = $"https://gort.dev/problems/{code.ToLowerInvariant()}"
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = traceId ?? context.HttpContext.TraceIdentifier;
        context.Result = new ObjectResult(problem) { StatusCode = status };
        context.ExceptionHandled = true;
    }

    private static (int Status, string Code, string Error) Map(Exception exception) => exception switch
    {
        ServiceNotFoundException ex => (StatusCodes.Status404NotFound, ex.ErrorCode, ex.Message),
        PermissionDeniedException ex => (StatusCodes.Status403Forbidden, ex.ErrorCode, ex.Message),
        TokenValidationException ex => (StatusCodes.Status401Unauthorized, ex.ErrorCode, ex.Message),
        ConfigurationException ex => (StatusCodes.Status400BadRequest, ex.ErrorCode, ex.Message),
        ArgumentException ex => (StatusCodes.Status400BadRequest, "INVALID_ARGUMENT", ex.Message),
        GortException ex => (StatusCodes.Status500InternalServerError, ex.ErrorCode, ex.Message),
        _ => (StatusCodes.Status500InternalServerError, "INTERNAL_ERROR", "Internal server error."),
    };
}
