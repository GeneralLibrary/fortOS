using System.Text.Json;
using FortOS.Installer.Core.Models;

namespace FortOS.Installer.Core.Tools;

/// <summary>
/// <c>lsblk --json</c> adapter: disk/partition enumeration (design draft 6).
/// Note: UUID reading is handled by <see cref="BlkidTool"/> (on loop devices, lsblk's
/// UUID column may not refresh after mkfs).
/// </summary>
public sealed class LsblkTool : ITool
{
    private readonly IProcessRunner _runner;

    public LsblkTool(IProcessRunner runner) => _runner = runner;

    public string Name => "lsblk";

    private static readonly string[] ListFields = ["NAME", "PATH", "SIZE", "MODEL", "SERIAL", "TRAN", "ROTA", "RM", "RO", "TYPE"];

    /// <summary>Enumerates physical disks (accepts disk and loop virtual disks; excludes partitions and virtual devices).</summary>
    public async Task<IReadOnlyList<DiskInfo>> ListDisksAsync(CancellationToken ct)
    {
        var result = await _runner
            .RunAsync("lsblk", ["--json", "-b", "-o", string.Join(',', ListFields)], ct)
            .ConfigureAwait(false);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(result.Stdout);
        }
        catch (JsonException ex)
        {
            throw new Exceptions.ToolException($"Failed to parse lsblk --json output: {ex.Message}", result.ExitCode, result.Stdout, result.Stderr, ex);
        }

        using (doc)
        {
            var disks = new List<DiskInfo>();
            if (doc.RootElement.TryGetProperty("blockdevices", out var devices))
            {
                foreach (var item in devices.EnumerateArray())
                {
                    // Accept disk and loop (virtual disk / QEMU disk install target); exclude partitions and virtual devices.
                    if (item.GetProperty("type").GetString() is not ("disk" or "loop"))
                    {
                        continue;
                    }

                    disks.Add(new DiskInfo
                    {
                        Name = item.GetProperty("name").GetString() ?? string.Empty,
                        Path = item.GetProperty("path").GetString() ?? string.Empty,
                        SizeBytes = item.TryGetUInt64("size", out var size) ? size : 0,
                        Model = item.GetStringOrNull("model"),
                        Serial = item.GetStringOrNull("serial"),
                        Transport = item.GetStringOrNull("tran"),
                        IsRotational = item.GetInt32OrZero("rota") == 1,
                        IsRemovable = item.GetInt32OrZero("rm") == 1,
                        IsReadOnly = item.GetInt32OrZero("ro") == 1,
                    });
                }
            }
            return disks;
        }
    }
}

/// <summary>System.Text.Json helper extensions.</summary>
internal static class JsonElementExtensions
{
    public static string? GetStringOrNull(this JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static int GetInt32OrZero(this JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;

    public static bool TryGetUInt64(this JsonElement element, string property, out ulong value)
    {
        if (element.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.Number)
        {
            value = prop.GetUInt64();
            return true;
        }
        value = 0;
        return false;
    }
}
