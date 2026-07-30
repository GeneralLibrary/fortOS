namespace GORT.Api.Middleware;

/// <summary>Maps v1 prefix to existing API routes centrally, avoiding duplicate routing for each action.</summary>
public sealed class ApiVersionCompatibilityMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value;
        if (path is not null && path.StartsWith("/api/v1/", StringComparison.OrdinalIgnoreCase))
            context.Request.Path = "/api/" + path[8..];
        else if (string.Equals(path, "/api/v1", StringComparison.OrdinalIgnoreCase))
            context.Request.Path = "/api";
        return next(context);
    }
}
