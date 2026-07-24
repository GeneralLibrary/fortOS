using System.Collections.Concurrent;

namespace GNAS.Api.Middleware;

/// <summary>固定窗口限流中间件。</summary>
public sealed class RateLimitMiddleware
{
    private readonly RequestDelegate next;
    private readonly IConfiguration configuration;
    private readonly ConcurrentDictionary<string, Counter> counters = new(StringComparer.Ordinal);

    /// <summary>初始化限流中间件。</summary>
    public RateLimitMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        this.next = next;
        this.configuration = configuration;
    }

    /// <summary>处理请求。</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var limit = IsLogin(context.Request.Path)
            ? configuration.GetValue("rateLimit:loginPerMinute", 5)
            : configuration.GetValue("rateLimit:defaultPerMinute", 100);
        var key = $"{context.Connection.RemoteIpAddress}|{(IsLogin(context.Request.Path) ? "login" : "default")}";
        var now = DateTimeOffset.UtcNow;
        var counter = counters.AddOrUpdate(key, _ => new Counter(now, 1), (_, old) => old.WindowStart.AddMinutes(1) <= now ? new Counter(now, 1) : old with { Count = old.Count + 1 });
        if (counter.Count > limit)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            var retry = Math.Max(1, (int)(counter.WindowStart.AddMinutes(1) - now).TotalSeconds);
            context.Response.Headers.RetryAfter = retry.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await context.Response.WriteAsJsonAsync(new { error = "请求过于频繁。", code = "RATE_LIMITED", traceId = context.Items["X-Trace-Id"] }, context.RequestAborted).ConfigureAwait(false);
            return;
        }

        await next(context).ConfigureAwait(false);
    }

    private static bool IsLogin(PathString path) => string.Equals(path.Value, "/api/auth/login", StringComparison.OrdinalIgnoreCase);

    private sealed record Counter(DateTimeOffset WindowStart, int Count);
}
