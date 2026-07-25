namespace GNAS.Core;

/// <summary>NAS 令牌管理器接口。</summary>
public interface ITokenManager
{
    /// <summary>签发令牌。</summary>
    Task<string> IssueTokenAsync(string subject, TokenType tokenType, IEnumerable<string> capabilities, int trustLevel, TimeSpan lifetime, IEnumerable<string>? delegationChain, string? deviceBinding, CancellationToken ct);
    /// <summary>验证令牌。</summary>
    Task<TokenValidationResult> ValidateTokenAsync(string token, CancellationToken ct);
    /// <summary>吊销令牌。</summary>
    Task RevokeTokenAsync(string jti, string reason, CancellationToken ct);
    /// <summary>续期令牌。</summary>
    Task<string> RenewTokenAsync(string token, CancellationToken ct);
    /// <summary>检查令牌是否已吊销。</summary>
    Task<bool> IsTokenRevokedAsync(string jti, CancellationToken ct);
}
