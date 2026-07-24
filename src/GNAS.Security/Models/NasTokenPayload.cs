using GNAS.Core;

namespace GNAS.Security.Models;

/// <summary>
/// 表示 NasToken 的结构化载荷。
/// </summary>
public sealed record NasTokenPayload
{
    /// <summary>签发者。</summary>
    public required string Iss { get; init; }
    /// <summary>主体。</summary>
    public required string Sub { get; init; }
    /// <summary>签发时间。</summary>
    public DateTimeOffset Iat { get; init; }
    /// <summary>过期时间。</summary>
    public DateTimeOffset Exp { get; init; }
    /// <summary>令牌类型。</summary>
    public TokenType TokenType { get; init; }
    /// <summary>信任级别，范围 0 到 5。</summary>
    public int TrustLevel { get; init; }
    /// <summary>能力集合。</summary>
    public NAbilitySet Capabilities { get; init; } = new();
    /// <summary>委托链。</summary>
    public string[] DelegationChain { get; init; } = [];
    /// <summary>设备绑定。</summary>
    public string? DeviceBinding { get; init; }
    /// <summary>JWT 标识。</summary>
    public required string Jti { get; init; }
}
