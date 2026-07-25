namespace GNAS.Api.Middleware;

/// <summary>把 v1 前缀集中映射到现有 API 路由，避免为每个 action 复制路由。</summary>
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
