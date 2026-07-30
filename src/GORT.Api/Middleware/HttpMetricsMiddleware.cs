using System.Diagnostics;
using GORT.Observability;

namespace GORT.Api.Middleware;

/// <summary>Records HTTP count, latency, and 5xx metrics.</summary>
public sealed class HttpMetricsMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, GortMetrics metrics)
    {
        var started = Stopwatch.GetTimestamp();
        try { await next(context).ConfigureAwait(false); }
        finally { metrics.RecordHttp(context.Request.Method, context.Response.StatusCode, Stopwatch.GetElapsedTime(started).TotalSeconds); }
    }
}
