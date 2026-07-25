using System.Diagnostics;
using GNAS.Observability;

namespace GNAS.Api.Middleware;

/// <summary>记录 HTTP 计数、时延和 5xx 指标。</summary>
public sealed class HttpMetricsMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, GnasMetrics metrics)
    {
        var started = Stopwatch.GetTimestamp();
        try { await next(context).ConfigureAwait(false); }
        finally { metrics.RecordHttp(context.Request.Method, context.Response.StatusCode, Stopwatch.GetElapsedTime(started).TotalSeconds); }
    }
}
