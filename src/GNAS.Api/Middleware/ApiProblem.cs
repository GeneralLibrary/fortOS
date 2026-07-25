using Microsoft.AspNetCore.Mvc;

namespace GNAS.Api.Middleware;

/// <summary>一致的 RFC7807 响应，所有错误均带有稳定 code 和 traceId。</summary>
public static class ApiProblem
{
    public static Task WriteAsync(HttpContext context, int status, string code, string detail)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = status >= 500 ? "Internal server error" : "Request failed",
            Detail = detail,
            Type = $"https://gnas.dev/problems/{code.ToLowerInvariant()}"
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = context.Items["X-Trace-Id"]?.ToString() ?? context.TraceIdentifier;
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(problem, context.RequestAborted);
    }
}
