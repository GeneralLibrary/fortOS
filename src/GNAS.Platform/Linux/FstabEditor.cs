using System.Text;

namespace GNAS.Platform.Linux;

/// <summary>
/// /etc/fstab content editor.
/// Provides a pure-functional entry add/remove, guaranteeing idempotence: only one GNAS-managed record per mount point,
/// and does not touch existing entries not managed by GNAS (root partition, swap, etc.).
/// </summary>
public static class FstabEditor
{
    /// <summary>Line-end marker for GNAS-managed entries, used to distinguish manually maintained entries.</summary>
    public const string ManagedMarker = "# gnas-managed";

    /// <summary>
    /// Inserts or updates a mount record.
    /// Replaces if a GNAS-managed entry already exists for the same mount point, otherwise appends to the end of the file.
    /// </summary>
    /// <param name="content">Existing fstab content.</param>
    /// <param name="device">Device path.</param>
    /// <param name="mountPoint">Mount point.</param>
    /// <param name="fsType">File system type.</param>
    /// <returns>Updated fstab content.</returns>
    public static string UpsertEntry(string content, string device, string mountPoint, string fsType)
    {
        var builder = new StringBuilder();
        foreach (var line in EnumerateLines(content))
        {
            if (!IsManagedEntryFor(line, mountPoint))
            {
                builder.AppendLine(line);
            }
        }

        builder.AppendLine($"{device} {mountPoint} {fsType} defaults,nofail 0 2 {ManagedMarker}");
        return builder.ToString();
    }

    /// <summary>
    /// Removes the GNAS-managed record for the specified mount point; unmanaged entries remain unchanged.
    /// </summary>
    /// <param name="content">Existing fstab content.</param>
    /// <param name="mountPoint">Mount point.</param>
    /// <returns>Updated fstab content.</returns>
    public static string RemoveEntry(string content, string mountPoint)
    {
        var builder = new StringBuilder();
        foreach (var line in EnumerateLines(content))
        {
            if (!IsManagedEntryFor(line, mountPoint))
            {
                builder.AppendLine(line);
            }
        }

        return builder.ToString();
    }

    private static IEnumerable<string> EnumerateLines(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            yield break;
        }

        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var count = lines.Length > 0 && lines[^1].Length == 0 ? lines.Length - 1 : lines.Length;
        for (var i = 0; i < count; i++)
        {
            yield return lines[i];
        }
    }

    private static bool IsManagedEntryFor(string line, string mountPoint)
    {
        if (!line.Contains(ManagedMarker, StringComparison.Ordinal))
        {
            return false;
        }

        var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return fields.Length >= 2 && string.Equals(fields[1], mountPoint, StringComparison.Ordinal);
    }
}
