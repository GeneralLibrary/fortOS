namespace FortOS.Core;

/// <summary>Unified list page request; prevents unbounded list responses.</summary>
public sealed record PageRequest(int Offset = 0, int Limit = 100)
{
    public const int MaximumLimit = 500;

    public int NormalizedOffset => Math.Max(0, Offset);
    public int NormalizedLimit => Math.Clamp(Limit, 1, MaximumLimit);
}

/// <summary>Unified paged response.</summary>
public sealed record Page<T>(IReadOnlyList<T> Items, int Offset, int Limit, long Total)
{
    public bool HasMore => Offset + Items.Count < Total;
}
