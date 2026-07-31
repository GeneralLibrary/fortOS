using FortOS.Core;

namespace FortOS.Security.Models;

/// <summary>
/// Represents the structured payload of a NasToken.
/// </summary>
public sealed record NasTokenPayload
{
    /// <summary>Issuer.</summary>
    public required string Iss { get; init; }
    /// <summary>Subject.</summary>
    public required string Sub { get; init; }
    /// <summary>Issued at.</summary>
    public DateTimeOffset Iat { get; init; }
    /// <summary>Expiration time.</summary>
    public DateTimeOffset Exp { get; init; }
    /// <summary>Token type.</summary>
    public TokenType TokenType { get; init; }
    /// <summary>Trust level, ranging from 0 to 5.</summary>
    public int TrustLevel { get; init; }
    /// <summary>Capability set.</summary>
    public NAbilitySet Capabilities { get; init; } = new();
    /// <summary>Delegation chain.</summary>
    public string[] DelegationChain { get; init; } = [];
    /// <summary>Device binding.</summary>
    public string? DeviceBinding { get; init; }
    /// <summary>JWT identifier.</summary>
    public required string Jti { get; init; }
}
