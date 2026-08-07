namespace FortOS.ServiceBus;

/// <summary>
/// Central service-bus tuning constants: supervisor backoff policy, health polling,
/// and shutdown timeouts.
/// </summary>
internal static class ServiceBusDefaults
{
    /// <summary>Grace period granted to ShutdownAllAsync on service stop.</summary>
    public static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Health monitor poll cadence.</summary>
    public static readonly TimeSpan HealthPollInterval = TimeSpan.FromSeconds(1);

    /// <summary>Supervisor poll interval while waiting for a service to become healthy.</summary>
    public static readonly TimeSpan HealthyWaitPollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>Crash-loop backoff counter resets after this much stable uptime.</summary>
    public static readonly TimeSpan BackoffResetAfter = TimeSpan.FromMinutes(10);

    /// <summary>Exponential backoff cap (seconds).</summary>
    public const int MaxBackoffSeconds = 60;

    /// <summary>Exponential backoff exponent cap (2^5 = 32s before reaching the cap).</summary>
    public const int MaxBackoffShift = 5;
}
