namespace FortOS.Api.Middleware;

/// <summary>
/// Adds standard defense-in-depth HTTP response headers to every response. These headers
/// reduce the blast radius of any future XSS/clickjacking bug in the dashboard SPA (for
/// example, they prevent the app from ever being framed by a third-party origin and stop
/// browsers from MIME-sniffing responses into an executable content type), without requiring
/// any change to the existing token-based authentication transport.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    /// <summary>Process the request, then attach security headers before the response is sent.</summary>
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "same-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            return Task.CompletedTask;
        });

        return next(context);
    }
}
