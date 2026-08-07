namespace FortOS.Core;

/// <summary>Identity service interface.</summary>
public interface IIdentityService
{
    /// <summary>Local username/password authentication.</summary>
    Task<AuthResult> AuthenticateLocalAsync(string username, string password, CancellationToken ct);
    /// <summary>TOTP two-factor authentication.</summary>
    Task<AuthResult> AuthenticateTotpAsync(string username, string code, CancellationToken ct);
    /// <summary>Service account authentication.</summary>
    Task<AuthResult> AuthenticateServiceAsync(string accountId, string apiKey, CancellationToken ct);
    /// <summary>Agent authentication.</summary>
    Task<AuthResult> AuthenticateAgentAsync(string agentId, string token, CancellationToken ct);
    /// <summary>Create a local user.</summary>
    Task<AuthResult> CreateLocalUserAsync(string username, string password, string? displayName, string? email, CancellationToken ct);
    /// <summary>Delete a local user.</summary>
    Task<AuthResult> DeleteLocalUserAsync(string username, CancellationToken ct);
}
