using System.Diagnostics;

namespace GORT.Api.Middleware;

/// <summary>Trace identifier middleware.</summary>
public sealed class TraceIdMiddleware
{
    private const string HeaderName = "X-Trace-Id";
    private readonly RequestDelegate next;
    private readonly ILogger<TraceIdMiddleware> logger;

    /// <summary>Initializes the trace identifier middleware.</summary>
    public TraceIdMiddleware(RequestDelegate next, ILogger<TraceIdMiddleware> logger)
    {
        this.next = next;
        this.logger = logger;
    }

    /// <summary>Process request.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var traceId = context.Request.Headers.TryGetValue(HeaderName, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.ToString()
            : Guid.CreateVersion7().ToString();
        context.Items[HeaderName] = traceId;
        Activity.Current?.SetTag("trace.id", traceId);
        Activity.Current?.AddBaggage(HeaderName, traceId);
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = traceId;
            return Task.CompletedTask;
        });
        using (logger.BeginScope(new Dictionary<string, object> { ["TraceId"] = traceId }))
        {
            await next(context).ConfigureAwait(false);
        }
    }
}
