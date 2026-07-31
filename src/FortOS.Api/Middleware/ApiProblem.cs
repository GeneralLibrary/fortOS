using Microsoft.AspNetCore.Mvc;

namespace FortOS.Api.Middleware;

/// <summary>Consistent RFC7807 response, all errors include a stable code and traceId.</summary>
public static class ApiProblem
{
    public static Task WriteAsync(HttpContext context, int status, string code, string detail)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = status >= 500 ? "Internal server error" : "Request failed",
            Detail = detail,
            Type = $"https://fortos.dev/problems/{code.ToLowerInvariant()}"
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = context.Items["X-Trace-Id"]?.ToString() ?? context.TraceIdentifier;
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(problem, context.RequestAborted);
    }
}
