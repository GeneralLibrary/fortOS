using System.Security.Claims;
using GNAS.Core;
using GNAS.Security.Models;
using Microsoft.Data.Sqlite;

namespace GNAS.Api.Middleware;

/// <summary>NAS token authentication middleware.</summary>
public sealed class NasTokenMiddleware
{
    private readonly RequestDelegate next;
    private readonly ILogger<NasTokenMiddleware> logger;

    /// <summary>Initializes the token middleware.</summary>
    public NasTokenMiddleware(RequestDelegate next, ILogger<NasTokenMiddleware> logger)
    {
        this.next = next;
        this.logger = logger;
    }

    /// <summary>Process request.</summary>
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
            if (context.GetEndpoint()?.Metadata.GetMetadata<GNAS.Api.Authorization.BootstrapOnlyAttribute>() is not null && await NoUsersExistAsync(database, context.RequestAborted).ConfigureAwait(false))
            {
                logger.LogWarning("No local users detected, API is in first-start anonymous bootstrap mode. Authentication will be required once a user is created.");
                await next(context).ConfigureAwait(false);
                return;
            }

            await UnauthorizedAsync(context, "Missing NAS token.", "TOKEN_MISSING").ConfigureAwait(false);
            return;
        }

        var validation = await tokenManager.ValidateTokenAsync(token, context.RequestAborted).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            await UnauthorizedAsync(context, validation.ErrorMessage ?? "Invalid token.", "TOKEN_INVALID").ConfigureAwait(false);
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
        await ApiProblem.WriteAsync(context, StatusCodes.Status401Unauthorized, code, error).ConfigureAwait(false);
    }
}
