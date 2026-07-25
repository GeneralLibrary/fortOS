using System.Security.Claims;
using GNAS.Core;
using GNAS.Security.Models;
using Microsoft.Data.Sqlite;

namespace GNAS.Api.Middleware;

/// <summary>NAS 令牌认证中间件。</summary>
public sealed class NasTokenMiddleware
{
    private readonly RequestDelegate next;
    private readonly ILogger<NasTokenMiddleware> logger;

    /// <summary>初始化令牌中间件。</summary>
    public NasTokenMiddleware(RequestDelegate next, ILogger<NasTokenMiddleware> logger)
    {
        this.next = next;
        this.logger = logger;
    }

    /// <summary>处理请求。</summary>
    public async Task InvokeAsync(HttpContext context, ITokenManager tokenManager, IDatabaseProvider database, IConfiguration configuration)
    {
        if (ShouldSkip(context.Request.Path))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var requireAuth = configuration.GetValue("security:require_auth", true);
        var token = ExtractToken(context.Request);
        if (!requireAuth && string.IsNullOrWhiteSpace(token))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            if (await NoUsersExistAsync(database, context.RequestAborted).ConfigureAwait(false))
            {
                logger.LogWarning("未检测到本地用户，API 处于首次启动匿名引导模式。创建用户后将自动要求认证。");
                await next(context).ConfigureAwait(false);
                return;
            }

            await UnauthorizedAsync(context, "缺少 NAS 令牌。", "TOKEN_MISSING").ConfigureAwait(false);
            return;
        }

        var validation = await tokenManager.ValidateTokenAsync(token, context.RequestAborted).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            await UnauthorizedAsync(context, validation.ErrorMessage ?? "令牌无效。", "TOKEN_INVALID").ConfigureAwait(false);
            return;
        }

        context.Items["NasTokenPayload"] = validation.Payload;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, validation.Subject ?? string.Empty),
            new(ClaimTypes.Name, validation.Subject ?? string.Empty),
        };
        claims.AddRange(validation.Capabilities.Select(c => new Claim("capability", c)));
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "NasToken"));
        await next(context).ConfigureAwait(false);
    }

    private static bool ShouldSkip(PathString path)
    {
        var value = path.Value ?? string.Empty;
        return value.Equals("/api/health", StringComparison.OrdinalIgnoreCase)
            || value.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/dashboard", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/grpc.reflection", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractToken(HttpRequest request)
    {
        var authorization = request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return authorization[7..].Trim();
        }

        return request.Headers.TryGetValue("X-Nas-Token", out var token) ? token.ToString() : null;
    }

    private static async Task<bool> NoUsersExistAsync(IDatabaseProvider database, CancellationToken ct)
    {
        try
        {
            await database.InitializeAsync(ct).ConfigureAwait(false);
            await using var connection = await database.GetConnectionAsync(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM users;";
            var count = (long)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? 0L);
            return count == 0;
        }
        catch (SqliteException)
        {
            return true;
        }
    }

    private static async Task UnauthorizedAsync(HttpContext context, string error, string code)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error, code, traceId = context.Items["X-Trace-Id"] }, context.RequestAborted).ConfigureAwait(false);
    }
}
