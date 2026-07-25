using System.Collections.Concurrent;
using GNAS.Security.Models;

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
        var subject = (context.Items["NasTokenPayload"] as NasTokenPayload)?.Sub;
        var identity = string.IsNullOrWhiteSpace(subject) ? context.Connection.RemoteIpAddress?.ToString() ?? "unknown" : $"subject:{subject}";
        var key = $"{identity}|{(IsLogin(context.Request.Path) ? "login" : "default")}";
        var now = DateTimeOffset.UtcNow;
        foreach (var expired in counters.Where(pair => pair.Value.WindowStart.AddMinutes(2) <= now).Select(pair => pair.Key).ToArray())
            counters.TryRemove(expired, out _);
        var counter = counters.AddOrUpdate(key, _ => new Counter(now, 1), (_, old) => old.WindowStart.AddMinutes(1) <= now ? new Counter(now, 1) : old with { Count = old.Count + 1 });
        if (counter.Count > limit)
        {
            var retry = Math.Max(1, (int)(counter.WindowStart.AddMinutes(1) - now).TotalSeconds);
            context.Response.Headers.RetryAfter = retry.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await ApiProblem.WriteAsync(context, StatusCodes.Status429TooManyRequests, "RATE_LIMITED", "请求过于频繁。").ConfigureAwait(false);
            return;
        }

        await next(context).ConfigureAwait(false);
    }

    private static bool IsLogin(PathString path) => string.Equals(path.Value, "/api/auth/login", StringComparison.OrdinalIgnoreCase);

    private sealed record Counter(DateTimeOffset WindowStart, int Count);
}
