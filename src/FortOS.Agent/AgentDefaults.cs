namespace FortOS.Agent;

/// <summary>
/// Central Agent tuning constants: issued-token lifetime and the renewal sweep policy.
/// </summary>
internal static class AgentDefaults
{
    /// <summary>Lifetime of tokens issued to agents.</summary>
    public static readonly TimeSpan AgentTokenLifetime = TimeSpan.FromHours(24);

    /// <summary>How often the renewal background sweep polls the registry.</summary>
    public static readonly TimeSpan RenewalPollInterval = TimeSpan.FromMinutes(1);

    /// <summary>Tokens expiring within this lead time are renewed proactively.</summary>
    public static readonly TimeSpan RenewalLeadTime = TimeSpan.FromHours(1);
}
