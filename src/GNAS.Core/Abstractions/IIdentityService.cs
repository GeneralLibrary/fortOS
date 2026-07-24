namespace GNAS.Core;

/// <summary>身份服务接口。</summary>
public interface IIdentityService
{
    /// <summary>本地用户名密码认证。</summary>
    Task<AuthResult> AuthenticateLocalAsync(string username, string password, CancellationToken ct);
    /// <summary>TOTP 二次认证。</summary>
    Task<AuthResult> AuthenticateTotpAsync(string username, string code, CancellationToken ct);
    /// <summary>LDAP 认证。</summary>
    Task<AuthResult> AuthenticateLdapAsync(string domain, string username, string password, CancellationToken ct);
    /// <summary>OAuth/OIDC 认证。</summary>
    Task<AuthResult> AuthenticateOAuthAsync(string provider, string authorizationCode, string? redirectUri, CancellationToken ct);
    /// <summary>服务账号认证。</summary>
    Task<AuthResult> AuthenticateServiceAsync(string accountId, string apiKey, CancellationToken ct);
    /// <summary>Agent 认证。</summary>
    Task<AuthResult> AuthenticateAgentAsync(string agentId, string token, CancellationToken ct);
    /// <summary>创建本地用户。</summary>
    Task<AuthResult> CreateLocalUserAsync(string username, string password, string? displayName, string? email, CancellationToken ct);
    /// <summary>删除本地用户。</summary>
    Task<AuthResult> DeleteLocalUserAsync(string username, CancellationToken ct);
}
