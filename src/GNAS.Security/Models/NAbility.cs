using System.Text.RegularExpressions;

namespace GNAS.Security.Models;

/// <summary>
/// 表示 GNAS 细粒度能力标识。
/// </summary>
public sealed record NAbility
{
    private static readonly Regex SegmentPattern = new("^[a-zA-Z0-9_.*-]+$", RegexOptions.Compiled);
    private readonly string[] _segments;

    /// <summary>
    /// 初始化能力标识。
    /// </summary>
    /// <param name="domain">能力域。</param>
    /// <param name="resource">资源名称。</param>
    /// <param name="action">动作名称。</param>
    /// <param name="scope">可选作用域。</param>
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
    /// 能力域。
    /// </summary>
    public string Domain { get; }

    /// <summary>
    /// 资源名称。
    /// </summary>
    public string Resource { get; }

    /// <summary>
    /// 动作名称。
    /// </summary>
    public string Action { get; }

    /// <summary>
    /// 可选作用域。
    /// </summary>
    public string? Scope { get; }

    /// <summary>
    /// 是否包含通配符。
    /// </summary>
    public bool IsWildcard => _segments.Any(static s => s is "*" or "**");

    /// <summary>
    /// 能力片段快照。
    /// </summary>
    public IReadOnlyList<string> Segments => _segments;

    /// <summary>
    /// 解析能力字符串。
    /// </summary>
    /// <param name="value">能力字符串。</param>
    /// <returns>能力标识。</returns>
    /// <exception cref="ArgumentException">能力字符串为空或格式无效。</exception>
    public static NAbility Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("NAbility 不能为空。", nameof(value));
        }

        var segments = value.Split(':', StringSplitOptions.None);
        var validLength = segments.Length is 3 or 4 || (segments.Length == 2 && segments[1] == "**");
        if (!validLength)
        {
            throw new ArgumentException("NAbility 必须为 domain:resource:action、domain:resource:scope:action 或 domain:** 格式。", nameof(value));
        }

        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment))
            {
                throw new ArgumentException("NAbility 片段不能为空。", nameof(value));
            }

            if (segment.Contains("**", StringComparison.Ordinal) && segment != "**")
            {
                throw new ArgumentException("NAbility 的 ** 通配符必须独占一个片段。", nameof(value));
            }

            if (!SegmentPattern.IsMatch(segment))
            {
                throw new ArgumentException($"NAbility 片段 '{segment}' 包含非法字符。", nameof(value));
            }
        }

        return new NAbility(segments);
    }

    /// <summary>
    /// 判断当前能力是否满足所需能力。
    /// </summary>
    /// <param name="required">所需能力。</param>
    /// <returns>满足时返回 true。</returns>
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
