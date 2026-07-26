namespace GNAS.Core;

/// <summary>NAS token manager interface.</summary>
public interface ITokenManager
{
    /// <summary>Issue a token.</summary>
    Task<string> IssueTokenAsync(string subject, TokenType tokenType, IEnumerable<string> capabilities, int trustLevel, TimeSpan lifetime, IEnumerable<string>? delegationChain, string? deviceBinding, CancellationToken ct);
    /// <summary>Validate a token.</summary>
    Task<TokenValidationResult> ValidateTokenAsync(string token, CancellationToken ct);
    /// <summary>Revoke a token.</summary>
    Task RevokeTokenAsync(string jti, string reason, CancellationToken ct);
    /// <summary>Renew a token.</summary>
    Task<string> RenewTokenAsync(string token, CancellationToken ct);
    /// <summary>Check whether a token is revoked.</summary>
    Task<bool> IsTokenRevokedAsync(string jti, CancellationToken ct);
    /// <summary>Generate and activate a new signing key; prior keys remain valid for existing tokens.</summary>
    Task<string> RotateSigningKeyAsync(CancellationToken ct);
}
