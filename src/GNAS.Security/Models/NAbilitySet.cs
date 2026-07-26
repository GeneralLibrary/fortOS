using System.Collections;
using System.Text.Json;

namespace GNAS.Security.Models;

/// <summary>
/// Represents a thread-safe mutable collection of NAbility.
/// </summary>
public sealed class NAbilitySet : IEnumerable<NAbility>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly object _sync = new();
    private readonly HashSet<NAbility> _items = [];

    /// <summary>
    /// Number of capabilities.
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
    /// Adds a capability.
    /// </summary>
    /// <param name="ability">Capability object.</param>
    public void Add(NAbility ability)
    {
        ArgumentNullException.ThrowIfNull(ability);
        lock (_sync)
        {
            _items.Add(ability);
        }
    }

    /// <summary>
    /// Adds a capability string.
    /// </summary>
    /// <param name="ability">Capability string.</param>
    public void Add(string ability) => Add(NAbility.Parse(ability));

    /// <summary>
    /// Determines whether the set satisfies the required capability.
    /// </summary>
    /// <param name="required">Required capability.</param>
    /// <returns>Returns true if satisfied.</returns>
    public bool Satisfies(NAbility required)
    {
        ArgumentNullException.ThrowIfNull(required);
        var snapshot = Snapshot();
        return snapshot.Any(candidate => candidate.Matches(required));
    }

    /// <summary>
    /// Determines whether the set satisfies the required capability string.
    /// </summary>
    /// <param name="required">Required capability string.</param>
    /// <returns>Returns true if satisfied.</returns>
    public bool Satisfies(string required) => Satisfies(NAbility.Parse(required));

    /// <summary>
    /// Determines whether the set satisfies all required capabilities.
    /// </summary>
    /// <param name="required">List of required capabilities.</param>
    /// <returns>Returns true if all are satisfied.</returns>
    public bool SatisfiesAll(IEnumerable<NAbility> required)
    {
        ArgumentNullException.ThrowIfNull(required);
        return required.All(Satisfies);
    }

    /// <summary>
    /// Merges another capability set.
    /// </summary>
    /// <param name="other">Another set.</param>
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
    /// Converts to a JSON string array.
    /// </summary>
    /// <returns>JSON string.</returns>
    public string ToJson() => JsonSerializer.Serialize(Snapshot().Select(static a => a.ToString()).ToArray(), JsonOptions);

    /// <summary>
    /// Creates a set from a JSON string array.
    /// </summary>
    /// <param name="json">JSON string.</param>
    /// <returns>Capability set.</returns>
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
