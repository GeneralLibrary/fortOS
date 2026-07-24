using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using GNAS.Core;
using GNAS.Platform.Execution;
using Microsoft.Extensions.Logging;

namespace GNAS.Platform.Linux;

/// <summary>
/// Linux 文件系统管理器。
/// </summary>
[SupportedOSPlatform("linux")]
public sealed partial class LinuxFileSystem : IFileSystem
{
    private static readonly HashSet<string> AllowedFileSystems = new(StringComparer.OrdinalIgnoreCase) { "ext4", "xfs", "btrfs" };
    private readonly CommandExecutor _executor;

    /// <summary>初始化 Linux 文件系统管理器。</summary>
    /// <param name="logger">日志记录器。</param>
    public LinuxFileSystem(ILogger<LinuxFileSystem> logger)
    {
        _executor = new CommandExecutor(logger);
    }

    /// <inheritdoc />
    public Task MountAsync(string device, string mountPoint, string fsType, CancellationToken ct)
    {
        ValidatePath(device, nameof(device));
        ValidatePath(mountPoint, nameof(mountPoint));
        ValidateFsType(fsType);
        return ExecuteIgnoreAsync("mount", $"-t {Quote(fsType)} {Quote(device)} {Quote(mountPoint)}", ct);
    }

    /// <inheritdoc />
    public Task UnmountAsync(string mountPoint, CancellationToken ct)
    {
        ValidatePath(mountPoint, nameof(mountPoint));
        return ExecuteIgnoreAsync("umount", Quote(mountPoint), ct);
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

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var property) && property.ValueKind != JsonValueKind.Null ? property.ToString() : null;

    private static void ValidateFsType(string fsType)
    {
        if (!AllowedFileSystems.Contains(fsType))
        {
            throw new ArgumentException("不支持或不安全的文件系统类型。", nameof(fsType));
        }
    }

    private static void ValidatePath(string path, string parameterName)
    {
        if (!SafePathRegex().IsMatch(path))
        {
            throw new ArgumentException("路径包含非法字符。", parameterName);
        }
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    [GeneratedRegex("^/[A-Za-z0-9_./@:+-]+$")]
    private static partial Regex SafePathRegex();
}
