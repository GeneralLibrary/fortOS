using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using GNAS.Core;
using GNAS.Platform.Execution;
using Microsoft.Extensions.Logging;

namespace GNAS.Platform.Windows;

/// <summary>
/// Windows 文件系统管理器。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsFileSystem : IFileSystem
{
    private readonly CommandExecutor _executor;

    /// <summary>初始化 Windows 文件系统管理器。</summary>
    /// <param name="logger">日志记录器。</param>
    public WindowsFileSystem(ILogger<WindowsFileSystem> logger) => _executor = new CommandExecutor(logger);

    /// <inheritdoc />
    public async Task MountAsync(string device, string mountPoint, string fsType, CancellationToken ct)
    {
        ValidatePath(mountPoint, nameof(mountPoint));
        await _executor.ExecuteAsync("mountvol", $"{Quote(mountPoint)} {Quote(device)}", ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UnmountAsync(string mountPoint, CancellationToken ct)
    {
        ValidatePath(mountPoint, nameof(mountPoint));
        await _executor.ExecuteAsync("mountvol", $"{Quote(mountPoint)} /D", ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task FormatAsync(string device, string fsType, CancellationToken ct)
    {
        ValidateFsType(fsType);
        await _executor.ExecuteAsync("format", $"{Quote(device)} /FS:{fsType} /Q /Y", ct, timeout: TimeSpan.FromMinutes(10)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<FsInfo> GetFilesystemInfoAsync(string mountPoint, CancellationToken ct)
    {
        ValidatePath(mountPoint, nameof(mountPoint));
        var drive = mountPoint.TrimEnd('\\', '/');
        var script = $"$ErrorActionPreference='Stop'; Get-Volume -DriveLetter '{drive[0]}' | Select-Object DriveLetter,FileSystem,Size,SizeRemaining | ConvertTo-Json";
        var result = await _executor.ExecuteAsync("powershell", $"-NoProfile -NonInteractive -Command {Quote(script)}", ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(result.Stdout);
        var total = GetLong(doc.RootElement, "Size");
        var avail = GetLong(doc.RootElement, "SizeRemaining");
        return new FsInfo { MountPoint = mountPoint, FileSystemType = GetString(doc.RootElement, "FileSystem") ?? string.Empty, TotalBytes = total, AvailableBytes = avail, UsedBytes = Math.Max(0, total - avail) };
    }

    private static void ValidateFsType(string fsType)
    {
        if (fsType is not ("NTFS" or "ReFS" or "exFAT")) throw new ArgumentException("不支持的文件系统类型。", nameof(fsType));
    }

    private static void ValidatePath(string path, string parameterName)
    {
        if (!PathRegex().IsMatch(path)) throw new ArgumentException("路径不安全。", parameterName);
    }

    private static string? GetString(JsonElement e, string n) => e.TryGetProperty(n, out var p) && p.ValueKind != JsonValueKind.Null ? p.ToString() : null;
    private static long GetLong(JsonElement e, string n) => e.TryGetProperty(n, out var p) && p.TryGetInt64(out var v) ? v : 0;
    private static string Quote(string value) => "\"" + value.Replace("\"", "`\"", StringComparison.Ordinal) + "\"";

    [GeneratedRegex("^[A-Za-z]:[\\\\/A-Za-z0-9_. -]*[\\\\/]?$")]
    private static partial Regex PathRegex();
}
