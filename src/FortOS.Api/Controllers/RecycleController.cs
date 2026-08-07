using System.Text;
using FortOS.Core;
using FortOS.Modules.Share.Services;
using Microsoft.AspNetCore.Mvc;

namespace FortOS.Api.Controllers;

/// <summary>Recycle bin controller.</summary>
[Route("api/recycle")]
public sealed class RecycleController : FortOSControllerBase
{
    private readonly RecycleBinService _recycleBin;
    private readonly FilePathResolver _resolver;

    /// <summary>Initializes the recycle bin controller.</summary>
    public RecycleController(RecycleBinService recycleBin, FilePathResolver resolver)
    {
        _recycleBin = recycleBin;
        _resolver = resolver;
    }

    /// <summary>List recycle bin contents.</summary>
    [HttpGet("{share}")]
    public object List(string share) => Directory.Exists(Path.Combine(share, ".recycle"))
        ? Directory.EnumerateFiles(Path.Combine(share, ".recycle"), "*", SearchOption.AllDirectories).Select(f => new { id = Convert.ToBase64String(Encoding.UTF8.GetBytes(f)), path = f, size = new FileInfo(f).Length })
        : Array.Empty<object>();

    /// <summary>Restore recycle bin file (compatible with old routes).</summary>
    [HttpPost("restore/{id}")]
    public async Task<object> RestoreLegacy(string id, [FromBody] RestoreRecycleRequest? request, CancellationToken ct)
    {
        // Legacy route carries no share segment; derive the share root from the
        // ".recycle" marker inside the encoded source path, then apply the same
        // safety checks as the parameterized route.
        var share = InferShareRoot(DecodeRecyclePath(id));
        return await RestoreCore(id, share, request?.TargetPath, ct).ConfigureAwait(false);
    }

    /// <summary>Restore recycle bin file.</summary>
    [HttpPost("{share}/restore/{id}")]
    public Task<object> Restore(string share, string id, [FromBody] RestoreRecycleRequest? request, CancellationToken ct)
        => RestoreCore(id, Path.GetFullPath(share), request?.TargetPath, ct);

    private async Task<object> RestoreCore(string id, string shareRoot, string? targetPath, CancellationToken ct)
    {
        // Security: both source and destination are attacker-influenced strings, so every
        // restore is constrained to the share directory. Real paths are resolved first so a
        // symlinked .recycle entry cannot escape the share via a lexical prefix check.
        var source = await _resolver.ResolveRealPathAsync(Path.GetFullPath(DecodeRecyclePath(id)), ct).ConfigureAwait(false);
        // The share root itself may be a symlink (e.g. /srv/nas -> /mnt/diskX/nas), so it must be
        // resolved to its real form too; comparing resolved paths against a lexical root would
        // reject every restore for such a share.
        var shareReal = await _resolver.ResolveRealPathAsync(Path.GetFullPath(shareRoot), ct).ConfigureAwait(false);
        if (!PathSafety.IsPathUnderRoot(source, Path.Combine(shareReal, ".recycle")))
        {
            throw new ArgumentException("Recycle bin item does not belong to the specified share path.", nameof(id));
        }

        if (!System.IO.File.Exists(source))
        {
            throw new FileNotFoundException("Recycle bin item no longer exists.", source);
        }

        var destination = string.IsNullOrWhiteSpace(targetPath) ? InferOriginalPath(source) : Path.GetFullPath(targetPath);
        destination = await _resolver.ResolveRealPathAsync(destination, ct).ConfigureAwait(false);
        if (!PathSafety.IsPathUnderRoot(destination, shareReal))
        {
            throw new ArgumentException("Restore target must stay within the share directory.", nameof(targetPath));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        System.IO.File.Move(source, destination, overwrite: true);
        return new { success = true };
    }

    /// <summary>Decodes a recycle-bin item id (base64 of the full source path).</summary>
    private static string DecodeRecyclePath(string id)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(id));
        }
        catch (FormatException)
        {
            throw new ArgumentException("Recycle bin item id is not a valid reference.", nameof(id));
        }
    }

    /// <summary>Empty recycle bin.</summary>
    [HttpDelete("empty")]
    public object EmptyRecycle([FromQuery] string share, [FromQuery] int retentionDays = 0)
        => new { deleted = _recycleBin.Cleanup(share, retentionDays) };

    /// <summary>Empty recycle bin by share path.</summary>
    [HttpDelete("{share}/empty")]
    public object EmptyRecycleByRoute(string share, [FromQuery] int retentionDays = 0)
        => new { deleted = _recycleBin.Cleanup(share, retentionDays) };

    /// <summary>Extracts the share root from a recycle bin path (the part before "/.recycle/").</summary>
    private static string InferShareRoot(string recyclePath)
    {
        var marker = $"{Path.DirectorySeparatorChar}.recycle{Path.DirectorySeparatorChar}";
        var index = recyclePath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index <= 0)
        {
            throw new ArgumentException("Invalid recycle bin path format, missing share root.", nameof(recyclePath));
        }

        return recyclePath[..index];
    }

    private static string InferOriginalPath(string recyclePath)
    {
        var marker = $"{Path.DirectorySeparatorChar}.recycle{Path.DirectorySeparatorChar}";
        var index = recyclePath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index <= 0)
        {
            throw new ArgumentException("Invalid recycle bin path format, cannot infer original path.", nameof(recyclePath));
        }

        var root = recyclePath[..index];
        var rest = recyclePath[(index + marker.Length)..];
        var slashIndex = rest.IndexOf(Path.DirectorySeparatorChar);
        if (slashIndex < 0 || slashIndex + 1 >= rest.Length)
        {
            throw new ArgumentException("Invalid recycle bin path format, missing original relative path.", nameof(recyclePath));
        }

        return Path.Combine(root, rest[(slashIndex + 1)..]);
    }
}
