using System.Collections;
using System.Text.Json;

namespace GNAS.Security.Models;

/// <summary>
/// 表示线程安全的 NAbility 可变集合。
/// </summary>
public sealed class NAbilitySet : IEnumerable<NAbility>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly object _sync = new();
    private readonly HashSet<NAbility> _items = [];

    /// <summary>
    /// 能力数量。
    /// </summary>
    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _items.Count;
            }
        }
    }

    /// <summary>
    /// 添加能力。
    /// </summary>
    /// <param name="ability">能力对象。</param>
    public void Add(NAbility ability)
    {
        ArgumentNullException.ThrowIfNull(ability);
        lock (_sync)
        {
            _items.Add(ability);
        }
    }

    /// <summary>
    /// 添加能力字符串。
    /// </summary>
    /// <param name="ability">能力字符串。</param>
    public void Add(string ability) => Add(NAbility.Parse(ability));

    /// <summary>
    /// 判断集合是否满足所需能力。
    /// </summary>
    /// <param name="required">所需能力。</param>
    /// <returns>满足时返回 true。</returns>
    public bool Satisfies(NAbility required)
    {
        ArgumentNullException.ThrowIfNull(required);
        var snapshot = Snapshot();
        return snapshot.Any(candidate => candidate.Matches(required));
    }

    /// <summary>
    /// 判断集合是否满足所需能力字符串。
    /// </summary>
    /// <param name="required">所需能力字符串。</param>
    /// <returns>满足时返回 true。</returns>
    public bool Satisfies(string required) => Satisfies(NAbility.Parse(required));

    /// <summary>
    /// 判断集合是否满足全部所需能力。
    /// </summary>
    /// <param name="required">所需能力列表。</param>
    /// <returns>全部满足时返回 true。</returns>
    public bool SatisfiesAll(IEnumerable<NAbility> required)
    {
        ArgumentNullException.ThrowIfNull(required);
        return required.All(Satisfies);
    }

    /// <summary>
    /// 合并另一能力集合。
    /// </summary>
    /// <param name="other">另一集合。</param>
    public void Merge(NAbilitySet other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var snapshot = other.Snapshot();
        lock (_sync)
        {
            foreach (var item in snapshot)
            {
                _items.Add(item);
            }
        }
    }

    /// <summary>
    /// 转换为 JSON 字符串数组。
    /// </summary>
    /// <returns>JSON 字符串。</returns>
    public string ToJson() => JsonSerializer.Serialize(Snapshot().Select(static a => a.ToString()).ToArray(), JsonOptions);

    /// <summary>
    /// 从 JSON 字符串数组创建集合。
    /// </summary>
    /// <param name="json">JSON 字符串。</param>
    /// <returns>能力集合。</returns>
    public static NAbilitySet FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new NAbilitySet();
        }

        var values = JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? [];
        var set = new NAbilitySet();
        foreach (var value in values)
        {
            set.Add(value);
        }

        return set;
    }

    /// <inheritdoc />
    public IEnumerator<NAbility> GetEnumerator() => ((IEnumerable<NAbility>)Snapshot()).GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private NAbility[] Snapshot()
    {
        lock (_sync)
        {
            return [.. _items];
        }
    }
}
