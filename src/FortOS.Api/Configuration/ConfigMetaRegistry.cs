// ============================================================================
// FortOS — Config Meta Registry
// ----------------------------------------------------------------------------
// Whitelist of user-editable configuration keys with semantic categories,
// control types, options and validation hints. Served to the dashboard via
// GET /api/config/meta so the settings UI can render friendly, categorized,
// typed controls instead of a raw key-value table.
//
// Only entries listed here are surfaced in the dashboard settings page;
// everything else returned by GET /api/config (environment plumbing, host
// bindings, etc.) stays hidden from casual editing.
// ============================================================================

using System.Collections.Generic;
using System.Linq;

namespace FortOS.Api.Configuration;

/// <summary>Control type the dashboard renders for a config entry.</summary>
public enum ConfigEntryType
{
    Boolean,
    Number,
    Select,
    String,
    Text,
}

/// <summary>Semantic category that groups config entries in the dashboard.</summary>
/// <param name="Id">Stable identifier referenced by <see cref="ConfigEntryMeta.Category"/>.</param>
/// <param name="Name">Display name (English canonical; dashboard localizes it).</param>
/// <param name="Icon">Icon identifier mapped to a dashboard icon set (e.g. "shield").</param>
/// <param name="Description">Optional one-line category description.</param>
/// <param name="Order">Sort order in the dashboard navigation.</param>
public sealed record ConfigCategoryMeta(string Id, string Name, string Icon, string? Description, int Order);

/// <summary>Metadata for one whitelisted, user-editable configuration entry.</summary>
/// <param name="Key">Configuration key as returned by GET /api/config.</param>
/// <param name="Category">Id of the owning <see cref="ConfigCategoryMeta"/>.</param>
/// <param name="Type">Control type rendered by the dashboard.</param>
/// <param name="Label">Canonical English label; dashboard localizes it when available.</param>
/// <param name="Description">Optional human-readable explanation shown under the label.</param>
/// <param name="Options">Choices for <see cref="ConfigEntryType.Select"/> entries.</param>
/// <param name="Min">Minimum value for <see cref="ConfigEntryType.Number"/> entries.</param>
/// <param name="Max">Maximum value for <see cref="ConfigEntryType.Number"/> entries.</param>
/// <param name="Step">Stepping for <see cref="ConfigEntryType.Number"/> entries.</param>
/// <param name="DefaultValue">Documented default (string form) for display/reference.</param>
/// <param name="Order">Sort order within the category.</param>
public sealed record ConfigEntryMeta(
    string Key,
    string Category,
    ConfigEntryType Type,
    string? Label,
    string? Description,
    IReadOnlyList<string>? Options = null,
    double? Min = null,
    double? Max = null,
    double? Step = null,
    string? DefaultValue = null,
    int Order = 0)
{
    /// <summary>Serialized control type used by the dashboard (lower-case enum name).</summary>
    public string TypeName => Type.ToString().ToLowerInvariant();
}

/// <summary>
/// Static registry of config categories and whitelisted, user-editable entries.
/// </summary>
public static class ConfigMetaRegistry
{
    /// <summary>Semantic categories shown as the settings page navigation.</summary>
    public static IReadOnlyList<ConfigCategoryMeta> Categories { get; } =
    [
        new("security", "Security", "shield", "Authentication and access control policies", 1),
        new("access", "Access Control", "speedometer", "Rate limiting for the API surface", 2),
        new("observability", "Monitoring & Logs", "pulse", "Metrics exposure and logging behaviour", 3),
        new("storage", "Disk & Storage", "server", "Disk health and RAID pool management", 4),
        new("advanced", "Advanced", "options", "Internal tuning options — change with care", 5),
    ];

    /// <summary>Whitelisted, user-editable configuration entries.</summary>
    public static IReadOnlyList<ConfigEntryMeta> Entries { get; } =
    [
        // ---- Security ----
        new("security:require_auth", "security", ConfigEntryType.Boolean, "Require Authentication",
            "Require a valid token on every API request. Turn off only for isolated or development deployments.",
            DefaultValue: "true", Order: 1),
        new("security:token:lifetime_minutes", "security", ConfigEntryType.Number, "Token Lifetime (minutes)",
            "How long an issued access token stays valid before renewal is required.",
            Min: 1, Max: 10080, Step: 5, DefaultValue: "480", Order: 2),

        // ---- Access control ----
        new("rateLimit:defaultPerMinute", "access", ConfigEntryType.Number, "Default Rate Limit (req/min)",
            "Maximum API requests per minute per client for non-login endpoints.",
            Min: 1, Max: 100000, Step: 10, DefaultValue: "100", Order: 1),
        new("rateLimit:loginPerMinute", "access", ConfigEntryType.Number, "Login Rate Limit (req/min)",
            "Maximum login attempts per minute. Keep low to slow down brute-force attacks.",
            Min: 1, Max: 10000, Step: 1, DefaultValue: "5", Order: 2),

        // ---- Observability ----
        new("metrics:allow_anonymous", "observability", ConfigEntryType.Boolean, "Anonymous Metrics",
            "Allow unauthenticated access to the /metrics endpoint for Prometheus scraping.",
            DefaultValue: "false", Order: 1),
        new("Serilog:MinimumLevel", "observability", ConfigEntryType.Select, "Log Level",
            "Minimum severity written to the log sink.",
            Options: ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"],
            DefaultValue: "Information", Order: 2),

        // ---- Advanced ----
        new("dashboard:enabled", "advanced", ConfigEntryType.Boolean, "Web Dashboard",
            "Serve the built-in management dashboard from this API instance.",
            DefaultValue: "false", Order: 1),
        new("idempotency:max_body_bytes", "advanced", ConfigEntryType.Number, "Idempotency Body Limit (bytes)",
            "Maximum request body size cached for idempotent retry handling.",
            Min: 1024, Max: 104_857_600, Step: 65_536, DefaultValue: "1048576", Order: 2),
        new("idempotency:ttl_minutes", "advanced", ConfigEntryType.Number, "Idempotency TTL (minutes)",
            "How long idempotency records are retained before expiry.",
            Min: 1, Max: 1440, Step: 5, DefaultValue: "60", Order: 3),
        new("agent:public_host", "advanced", ConfigEntryType.String, "Agent Public Host",
            "Externally reachable host or URL advertised to agents for callback traffic.",
            Order: 4),
    ];

    /// <summary>True if the key is whitelisted for dashboard editing.</summary>
    public static bool IsWhitelisted(string key)
        => Entries.Any(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));

    private static readonly string[] SensitiveAnySegment =
        ["password", "pass", "secret", "credential"];

    private static readonly string[] SensitiveLastSegment =
        ["token", "key"];

    /// <summary>
    /// A key is sensitive when it names a credential. Credential words
    /// (<c>password</c>, <c>secret</c>, …) match in any path segment, so
    /// <c>store:secret:path</c> is hidden even when its last segment is generic.
    /// Namespace words (<c>token</c>, <c>key</c>) only match as the last segment,
    /// so <c>security:token:lifetime_minutes</c> (a duration, not a secret) stays
    /// visible while <c>security:token</c> / <c>agent:api_key</c> are hidden.
    /// </summary>
    public static bool IsSensitive(string key)
    {
        var segments = key.Split(':');
        var last = segments[^1];
        if (SensitiveAnySegment.Any(s => segments.Any(seg => seg.Contains(s, StringComparison.OrdinalIgnoreCase))))
            return true;
        return SensitiveLastSegment.Any(s => last.Contains(s, StringComparison.OrdinalIgnoreCase));
    }
}
