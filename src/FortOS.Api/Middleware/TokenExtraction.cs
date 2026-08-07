using Microsoft.AspNetCore.Http;

namespace FortOS.Api.Middleware;

/// <summary>
/// Single source of truth for extracting the NAS token from an incoming HTTP request.
/// Previously duplicated in the auth middleware, the controller base class, and the
/// capability authorization filter; centralizing keeps the header contract in one place.
/// </summary>
public static class TokenExtraction
{
    /// <summary>
    /// Reads the NAS token from the Authorization (Bearer) header, falling back to the
    /// X-Nas-Token header. Returns null when neither header carries a token.
    /// </summary>
    public static string? FromRequest(HttpRequest request)
    {
        var authorization = request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return authorization[7..].Trim();
        }

        return request.Headers.TryGetValue("X-Nas-Token", out var token) ? token.ToString() : null;
    }
}
