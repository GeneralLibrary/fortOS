using System.Text.RegularExpressions;

namespace GORT.Security.Models;

/// <summary>
/// Represents a GORT fine-grained capability identifier.
/// </summary>
public sealed record NAbility
{
    private static readonly Regex SegmentPattern = new("^[a-zA-Z0-9_.*-]+$", RegexOptions.Compiled);
    private readonly string[] _segments;

    /// <summary>
    /// Initialize the capability identifier.
    /// </summary>
    /// <param name="domain">Capability domain.</param>
    /// <param name="resource">Resource name.</param>
    /// <param name="action">Action name.</param>
    /// <param name="scope">Optional scope.</param>
    public NAbility(string domain, string resource, string action, string? scope = null)
        : this(scope is null ? [domain, resource, action] : [domain, resource, scope, action])
    {
    }

    private NAbility(string[] segments)
    {
        _segments = segments;
        Domain = segments[0];
        Resource = segments.Length > 1 ? segments[1] : string.Empty;
        Action = segments[^1];
        Scope = segments.Length == 4 ? segments[2] : null;
    }

    /// <summary>
    /// Capability domain.
    /// </summary>
    public string Domain { get; }

    /// <summary>
    /// Resource name.
    /// </summary>
    public string Resource { get; }

    /// <summary>
    /// Action name.
    /// </summary>
    public string Action { get; }

    /// <summary>
    /// Optional scope.
    /// </summary>
    public string? Scope { get; }

    /// <summary>
    /// Whether it contains wildcards.
    /// </summary>
    public bool IsWildcard => _segments.Any(static s => s is "*" or "**");

    /// <summary>
    /// Capability segment snapshot.
    /// </summary>
    public IReadOnlyList<string> Segments => _segments;

    /// <summary>
    /// Parses a capability string.
    /// </summary>
    /// <param name="value">Capability string.</param>
    /// <returns>Capability identifier.</returns>
    /// <exception cref="ArgumentException">Capability string is empty or has an invalid format.</exception>
    public static NAbility Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("NAbility cannot be empty.", nameof(value));
        }

        var segments = value.Split(':', StringSplitOptions.None);
        var validLength = segments.Length is 3 or 4 || (segments.Length == 2 && segments[1] == "**");
        if (!validLength)
        {
            throw new ArgumentException("NAbility must be in domain:resource:action, domain:resource:scope:action, or domain:** format.", nameof(value));
        }

        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment))
            {
                throw new ArgumentException("NAbility segment cannot be empty.", nameof(value));
            }

            if (segment.Contains("**", StringComparison.Ordinal) && segment != "**")
            {
                throw new ArgumentException("NAbility ** wildcard must occupy a segment by itself.", nameof(value));
            }

            if (!SegmentPattern.IsMatch(segment))
            {
                throw new ArgumentException($"NAbility segment '{segment}' contains illegal characters.", nameof(value));
            }
        }

        return new NAbility(segments);
    }

    /// <summary>
    /// Determines whether the current capability satisfies the required capability.
    /// </summary>
    /// <param name="required">Required capability.</param>
    /// <returns>Returns true if satisfied.</returns>
    public bool Matches(NAbility required) => MatchSegments(_segments, 0, required._segments, 0);

    /// <inheritdoc />
    public override string ToString() => string.Join(':', _segments);

    /// <inheritdoc />
    public bool Equals(NAbility? other)
        => other is not null && _segments.SequenceEqual(other._segments, StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var segment in _segments)
        {
            hash.Add(segment, StringComparer.OrdinalIgnoreCase);
        }

        return hash.ToHashCode();
    }

    private static bool MatchSegments(IReadOnlyList<string> pattern, int patternIndex, IReadOnlyList<string> required, int requiredIndex)
    {
        while (true)
        {
            if (patternIndex == pattern.Count)
            {
                return requiredIndex == required.Count;
            }

            var current = pattern[patternIndex];
            if (current == "**")
            {
                if (patternIndex == pattern.Count - 1)
                {
                    return true;
                }

                for (var i = requiredIndex; i <= required.Count; i++)
                {
                    if (MatchSegments(pattern, patternIndex + 1, required, i))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (requiredIndex == required.Count)
            {
                return false;
            }

            if (current != "*" && !string.Equals(current, required[requiredIndex], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            patternIndex++;
            requiredIndex++;
        }
    }
}
