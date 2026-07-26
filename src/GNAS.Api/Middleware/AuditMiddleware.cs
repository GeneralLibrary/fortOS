using GNAS.Core;
using Microsoft.Extensions.Logging;

namespace GNAS.Api.Middleware;

/// <summary>Audit log middleware.</summary>
public sealed class AuditMiddleware
{
    private readonly RequestDelegate next;

    /// <summary>Initializes the audit middleware.</summary>
    public AuditMiddleware(RequestDelegate next) => this.next = next;

    /// <summary>Process request.</summary>
    public async Task InvokeAsync(HttpContext context, ILogPipeline pipeline)
    {
        await next(context).ConfigureAwait(false);
        if (ShouldSkip(context.Request.Path)) return;

        var action = $"{context.Request.Method} {context.Request.Path}";
        var userId = context.User.Identity?.IsAuthenticated == true ? context.User.Identity.Name : null;
        var entry = new LogEntry
        {
            Category = LogCategory.Audit,
            Level = context.Response.StatusCode < 400 ? LogLevel.Information : LogLevel.Warning,
            SourceComponent = "GNAS.Api",
            UserId = userId,
            TraceId = context.Items["X-Trace-Id"]?.ToString(),
            Message = action,
            Audit = new AuditDetail
            {
                Action = action,
                Resource = context.Request.Path,
                ResourceType = "http",
                Granted = context.Response.StatusCode < 400,
                ClientIp = context.Connection.RemoteIpAddress?.ToString(),
                UserAgent = context.Request.Headers.UserAgent.ToString(),
                CurrentHash = string.Empty,
                ChainSignature = string.Empty,
            },
        };
        await pipeline.ProcessAsync(entry, context.RequestAborted).ConfigureAwait(false);
    }

    private static bool ShouldSkip(PathString path)
    {
        var value = path.Value ?? string.Empty;
        return value.Equals("/api/health", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/dashboard", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase);
    }
}
