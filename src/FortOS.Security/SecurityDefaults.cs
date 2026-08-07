namespace FortOS.Security;

/// <summary>
/// Central security tuning constants: token lifetimes and login lockout policy.
/// Kept in one place so authentication behavior is consistent across services.
/// </summary>
internal static class SecurityDefaults
{
    /// <summary>Lifetime of interactive user session tokens.</summary>
    public static readonly TimeSpan SessionTokenLifetime = TimeSpan.FromHours(8);

    /// <summary>Lifetime of service-account tokens.</summary>
    public static readonly TimeSpan ServiceTokenLifetime = TimeSpan.FromHours(1);

    /// <summary>Failed login attempts before the account is locked.</summary>
    public const int MaxLoginFailures = 5;

    /// <summary>How long an account stays locked after exceeding <see cref="MaxLoginFailures"/>.</summary>
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
}
