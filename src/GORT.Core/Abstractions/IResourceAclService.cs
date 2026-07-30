namespace GORT.Core;

/// <summary>Persistent resource ACL administration.</summary>
public interface IResourceAclService
{
    Task SetAsync(string resourcePath, string principal, IEnumerable<string> capabilities, CancellationToken ct);
    Task RemoveAsync(string resourcePath, string principal, CancellationToken ct);
    Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetAsync(string resourcePath, CancellationToken ct);
}
