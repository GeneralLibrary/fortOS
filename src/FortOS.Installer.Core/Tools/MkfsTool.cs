using FortOS.Installer.Core.Models;

namespace FortOS.Installer.Core.Tools;

/// <summary>
/// <c>mkfs.*</c> / <c>mkswap</c> 适配器:文件系统格式化。
/// </summary>
public sealed class MkfsTool : ITool
{
    private static readonly TimeSpan FormatTimeout = TimeSpan.FromMinutes(10);
    private readonly IProcessRunner _runner;

    public MkfsTool(IProcessRunner runner) => _runner = runner;

    public string Name => "mkfs";

    /// <summary>格式化设备为指定文件系统。label 为空时省略卷标参数。</summary>
    public async Task FormatAsync(string device, PartitionFs fs, string? label, CancellationToken ct)
    {
        (var fileName, var args) = BuildCommand(device, fs, label);
        await _runner.RunAsync(fileName, args, ct, timeout: FormatTimeout).ConfigureAwait(false);
    }

    private static (string FileName, List<string> Args) BuildCommand(string device, PartitionFs fs, string? label)
    {
        var args = new List<string>();
        var fileName = fs switch
        {
            PartitionFs.Vfat => "mkfs.fat",
            PartitionFs.Ext4 => "mkfs.ext4",
            PartitionFs.Btrfs => "mkfs.btrfs",
            PartitionFs.Xfs => "mkfs.xfs",
            PartitionFs.Swap => "mkswap",
            PartitionFs.None => throw new InvalidOperationException("PartitionFs.None cannot be formatted."),
            _ => throw new InvalidOperationException($"Unsupported file system: {fs}"),
        };

        switch (fs)
        {
            case PartitionFs.Vfat:
                args.Add("-F");
                args.Add("32");
                break;
            case PartitionFs.Ext4:
            case PartitionFs.Xfs:
                args.Add("-f"); // force,覆盖残留签名
                break;
            case PartitionFs.Btrfs:
                args.Add("-f");
                break;
            case PartitionFs.Swap:
                break;
        }

        if (!string.IsNullOrEmpty(label))
        {
            // dosfstools 用 -n 设卷标;ext4/btrfs/xfs/swap 用 -L。
            args.Add(fs == PartitionFs.Vfat ? "-n" : "-L");
            args.Add(label);
        }

        args.Add(device);
        return (fileName, args);
    }
}
