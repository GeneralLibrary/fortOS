using System.Text;

namespace GNAS.Platform.Linux;

/// <summary>
/// /etc/fstab 内容编辑器。
/// 提供纯函数式的条目增删，保证幂等：同一挂载点只保留一条 GNAS 管理的记录，
/// 且不触碰非 GNAS 管理的既有条目（根分区、swap 等）。
/// </summary>
public static class FstabEditor
{
    /// <summary>GNAS 托管条目的行尾标记，用于区分手工维护的条目。</summary>
    public const string ManagedMarker = "# gnas-managed";

    /// <summary>
    /// 插入或更新一条挂载记录。
    /// 若同一挂载点已存在 GNAS 托管条目则替换，否则追加到文件末尾。
    /// </summary>
    /// <param name="content">现有 fstab 内容。</param>
    /// <param name="device">设备路径。</param>
    /// <param name="mountPoint">挂载点。</param>
    /// <param name="fsType">文件系统类型。</param>
    /// <returns>更新后的 fstab 内容。</returns>
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
    /// 移除指定挂载点的 GNAS 托管记录；非托管条目保持原样。
    /// </summary>
    /// <param name="content">现有 fstab 内容。</param>
    /// <param name="mountPoint">挂载点。</param>
    /// <returns>更新后的 fstab 内容。</returns>
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
