using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using GORT.Core;
using GORT.Platform.Execution;
using Microsoft.Extensions.Logging;

namespace GORT.Platform.Linux;

/// <summary>
/// Linux file system manager.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed partial class LinuxFileSystem : IFileSystem
{
    private static readonly HashSet<string> AllowedFileSystems = new(StringComparer.OrdinalIgnoreCase) { "ext4", "xfs", "btrfs" };
    private const string FstabPath = "/etc/fstab";
    private static readonly SemaphoreSlim FstabGate = new(1, 1);
    private readonly CommandExecutor _executor;
    private readonly ILogger _logger;

    /// <summary>Initializes the Linux file system manager.</summary>
    /// <param name="logger">Logger.</param>
    public LinuxFileSystem(ILogger<LinuxFileSystem> logger)
    {
        _executor = new CommandExecutor(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task MountAsync(string device, string mountPoint, string fsType, CancellationToken ct)
    {
        ValidatePath(device, nameof(device));
        ValidatePath(mountPoint, nameof(mountPoint));
        ValidateFsType(fsType);
        await ExecuteIgnoreAsync("mount", $"-t {Quote(fsType)} {Quote(device)} {Quote(mountPoint)}", ct).ConfigureAwait(false);
        await PersistFstabAsync(content => FstabEditor.UpsertEntry(content, device, mountPoint, fsType.ToLowerInvariant()), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UnmountAsync(string mountPoint, CancellationToken ct)
    {
        ValidatePath(mountPoint, nameof(mountPoint));
        await ExecuteIgnoreAsync("umount", Quote(mountPoint), ct).ConfigureAwait(false);
        await PersistFstabAsync(content => FstabEditor.RemoveEntry(content, mountPoint), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task FormatAsync(string device, string fsType, CancellationToken ct)
    {
        ValidatePath(device, nameof(device));
        ValidateFsType(fsType);
        return ExecuteIgnoreAsync($"mkfs.{fsType.ToLowerInvariant()}", Quote(device), ct);
    }

    /// <inheritdoc />
    public async Task<FsInfo> GetFilesystemInfoAsync(string mountPoint, CancellationToken ct)
    {
        ValidatePath(mountPoint, nameof(mountPoint));
        var findmnt = await _executor.ExecuteAsync("findmnt", $"--json --target {Quote(mountPoint)}", ct).ConfigureAwait(false);
        var fsType = string.Empty;
        using (var doc = JsonDocument.Parse(findmnt.Stdout))
        {
            if (doc.RootElement.TryGetProperty("filesystems", out var fileSystems) && fileSystems.GetArrayLength() > 0)
            {
                fsType = GetString(fileSystems[0], "fstype") ?? string.Empty;
            }
        }

        var df = await _executor.ExecuteAsync("df", $"-B1 --output=size,used,avail {Quote(mountPoint)}", ct).ConfigureAwait(false);
        var lines = df.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        long total = 0, used = 0, available = 0;
        if (lines.Length >= 2)
        {
            var parts = Regex.Split(lines[1].Trim(), "\\s+");
            if (parts.Length >= 3)
            {
                long.TryParse(parts[0], out total);
                long.TryParse(parts[1], out used);
                long.TryParse(parts[2], out available);
            }
        }

        return new FsInfo { MountPoint = mountPoint, FileSystemType = fsType, TotalBytes = total, UsedBytes = used, AvailableBytes = available };
    }

    private async Task ExecuteIgnoreAsync(string command, string arguments, CancellationToken ct)
    {
        await _executor.ExecuteAsync(command, arguments, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Persists mount changes to /etc/fstab, ensuring they survive reboot.
    /// Persistence failure (e.g., read-only /etc inside a container) only logs a warning and does not affect the current mount operation.
    /// </summary>
    private async Task PersistFstabAsync(Func<string, string> transform, CancellationToken ct)
    {
        await FstabGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var content = File.Exists(FstabPath) ? await File.ReadAllTextAsync(FstabPath, ct).ConfigureAwait(false) : string.Empty;
            await File.WriteAllTextAsync(FstabPath, transform(content), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Unable to update {FstabPath}, mount will not be automatically restored after reboot.", FstabPath);
        }
        finally
        {
            FstabGate.Release();
        }
    }

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var property) && property.ValueKind != JsonValueKind.Null ? property.ToString() : null;

    private static void ValidateFsType(string fsType)
    {
        if (!AllowedFileSystems.Contains(fsType))
        {
            throw new ArgumentException("Unsupported or unsafe file system type.", nameof(fsType));
        }
    }

    private static void ValidatePath(string path, string parameterName)
    {
        if (!SafePathRegex().IsMatch(path))
        {
            throw new ArgumentException("Path contains illegal characters.", parameterName);
        }
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    [GeneratedRegex("^/[A-Za-z0-9_./@:+-]+$")]
    private static partial Regex SafePathRegex();
}
